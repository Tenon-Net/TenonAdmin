using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置实例运行态服务。发起走引擎;<c>formComponent</c> 从版本模型透出供前端挂载。
/// </summary>
public class WfInstanceService(
    IRepository<WfInstance> instances,
    IRepository<WfDefinition> definitions,
    IRepository<WfDefinitionVersion> versions,
    IRepository<WfHistory> histories,
    IRepository<WfHisTask> hisTasks,
    IRepository<WfTask> tasks,
    IRepository<WfTaskActor> actors,
    IRepository<WfCc> ccs,
    IRepository<SysUserRole> userRoles,
    IWorkflowEngine engine) : IWfInstanceService
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<WfStartableDefinitionOutput>> ListStartableAsync(
        long userId,
        long? orgId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.StarterInvalid);

        var defs = await definitions.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(d => d.Status == WfDefinitionStatus.Published && d.CurrentVersion >= 1)
            .OrderBy(d => d.Name, OrderByType.Asc)
            .ToListAsync();
        if (defs.Count == 0)
            return [];

        var defIds = defs.Select(d => d.Id).ToList();
        var snapshotMap = (await versions.AsQueryable()
                .Where(v => defIds.Contains(v.DefinitionId) && v.Version >= 1)
                .ToListAsync())
            .GroupBy(v => v.DefinitionId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(v => v.Version));
        var roleIds = await GetRoleIdsAsync(userId, cancellationToken);
        var result = new List<WfStartableDefinitionOutput>();

        foreach (var def in defs)
        {
            if (!snapshotMap.TryGetValue(def.Id, out var byVersion)
                || !byVersion.TryGetValue(def.CurrentVersion, out var snapshot))
                continue;
            var model = WfModelJson.Deserialize(snapshot.ModelJson)
                        ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);
            if (!CanStart(model, userId, orgId, roleIds))
                continue;
            result.Add(new WfStartableDefinitionOutput
            {
                Id = def.Id,
                Name = def.Name,
                Icon = def.Icon,
                GroupName = def.GroupName,
                Version = snapshot.Version,
                FormComponent = model.FormComponent,
            });
        }

        return result;
    }

    /// <inheritdoc />
    public virtual async Task<WfStartableDefinitionDetailOutput> GetStartableAsync(
        long definitionId,
        long userId,
        long? orgId,
        CancellationToken cancellationToken = default)
    {
        var (def, version, model) = await RequireStartableAsync(
            definitionId, userId, orgId, cancellationToken);
        return new WfStartableDefinitionDetailOutput
        {
            Id = def.Id,
            Name = def.Name,
            Icon = def.Icon,
            GroupName = def.GroupName,
            Version = version.Version,
            FormComponent = model.FormComponent,
            DefinitionVersionId = version.Id,
            Model = model,
        };
    }

    /// <inheritdoc />
    public virtual async Task<WfEngineResult> StartAsync(
        WfStartInput input,
        long starterUserId,
        long? starterOrgId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.DefinitionId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);
        if (starterUserId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.StarterInvalid);

        var (_, version, _) = await RequireStartableAsync(
            input.DefinitionId, starterUserId, starterOrgId, cancellationToken);
        var businessKey = string.IsNullOrWhiteSpace(input.BusinessKey) ? null : input.BusinessKey.Trim();
        if (businessKey?.Length > 128)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelFieldTooLong,
                new Dictionary<string, object?> { ["field"] = "businessKey", ["maxLength"] = 128 });
        }

        return await engine.ExecuteAsync(
            new StartInstanceCmd
            {
                DefinitionVersionId = version.Id,
                StarterUserId = starterUserId,
                StarterOrgId = starterOrgId,
                BusinessKey = businessKey,
                VariablesJson = input.VariablesJson,
                SelectedUserIdsByNode = input.SelectedUserIdsByNode,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<WfInstanceListItemOutput>> PageMineAsync(
        long starterUserId,
        WfInstancePageInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // DefinitionId 过滤:实例只存 versionId,先解析该定义下已发布版本 Id 集。
        List<long>? versionIds = null;
        if (input.DefinitionId is > 0)
        {
            versionIds = await versions.AsQueryable()
                .Where(v => v.DefinitionId == input.DefinitionId.Value && v.Version >= 1)
                .Select(v => v.Id)
                .ToListAsync();
            if (versionIds.Count == 0)
            {
                return new PagedList<WfInstanceListItemOutput>
                {
                    Current = input.Current <= 0 ? 1 : input.Current,
                    Size = input.Size <= 0 ? 20 : input.Size,
                    Total = 0,
                    Items = [],
                };
            }
        }

        // 先分页实例,再补定义名/版本号(避免多表 Join 与数据范围过滤器缠绕)。
        var page = await instances.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.StarterUserId == starterUserId)
            .WhereIF(input.Status.HasValue, i => i.Status == input.Status!.Value)
            .WhereIF(versionIds is not null, i => versionIds!.Contains(i.DefinitionVersionId))
            .WhereIF(!string.IsNullOrWhiteSpace(input.BusinessKey), i => i.BusinessKey == input.BusinessKey)
            .ToPagedListAsync(input, q => q.OrderBy(i => i.Id, OrderByType.Desc));

        return await MapInstancePageAsync(page, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<WfInstanceDetailOutput> GetAsync(
        long instanceId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = await RequireInstanceAsync(instanceId);
        await EnsureParticipantAsync(instance, currentUserId, cancellationToken);

        var version = await versions.AsQueryable()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync()
            ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var def = await definitions.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .ClearFilter<ISoftDelete>()
            .Where(d => d.Id == version.DefinitionId)
            .FirstAsync();
        var model = WfModelJson.Deserialize(version.ModelJson);

        var his = await hisTasks.AsQueryable()
            .Where(h => h.InstanceId == instance.Id)
            .OrderBy(h => h.CreateTime, OrderByType.Asc)
            .ToListAsync();

        WfTodoItemOutput? myPending = null;
        if (currentUserId > 0 && instance.Status == WfInstanceStatus.Running)
        {
            myPending = await FindMyPendingAsync(instance.Id, currentUserId, def?.Name ?? "", version.DefinitionId, cancellationToken);
        }

        return new WfInstanceDetailOutput
        {
            Id = instance.Id,
            DefinitionId = version.DefinitionId,
            DefinitionName = def?.Name ?? "",
            DefinitionVersionId = version.Id,
            Version = version.Version,
            BusinessKey = instance.BusinessKey,
            StarterUserId = instance.StarterUserId,
            Status = instance.Status,
            VariablesJson = instance.VariablesJson,
            FormComponent = model?.FormComponent,
            CreateTime = instance.CreateTime,
            MyPendingTask = myPending,
            HisTasks = his.Select(MapHisTask).ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<WfHistoryItemOutput>> ListHistoryAsync(
        long instanceId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = await RequireInstanceAsync(instanceId);
        await EnsureParticipantAsync(instance, currentUserId, cancellationToken);

        var list = await histories.AsQueryable()
            .Where(h => h.InstanceId == instanceId)
            .OrderBy(h => h.CreateTime, OrderByType.Asc)
            .OrderBy(h => h.Id, OrderByType.Asc)
            .ToListAsync();

        return list.Select(h => new WfHistoryItemOutput
        {
            Id = h.Id,
            EventType = h.EventType,
            NodeId = h.NodeId,
            PayloadJson = h.PayloadJson,
            CreateTime = h.CreateTime,
        }).ToList();
    }

    /// <summary>按 Id 取实例;不存在抛 <see cref="WorkflowErrorCode.InstanceNotFound"/>。</summary>
    protected virtual async Task<WfInstance> RequireInstanceAsync(long id)
    {
        if (id <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        var instance = await instances.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == id)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        return instance;
    }

    protected virtual async Task<PagedList<WfInstanceListItemOutput>> MapInstancePageAsync(
        PagedList<WfInstance> page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (page.Items.Count == 0)
        {
            return new PagedList<WfInstanceListItemOutput>
            {
                Current = page.Current,
                Size = page.Size,
                Total = page.Total,
                Items = [],
            };
        }

        var versionIds = page.Items.Select(i => i.DefinitionVersionId).Distinct().ToList();
        var versionRows = await versions.AsQueryable()
            .Where(v => versionIds.Contains(v.Id))
            .ToListAsync();
        var versionMap = versionRows.ToDictionary(v => v.Id);

        var defIds = versionRows.Select(v => v.DefinitionId).Distinct().ToList();
        var defMap = (await definitions.AsQueryable()
                .ClearFilter<IOrgScoped>()
                .Where(d => defIds.Contains(d.Id))
                .ToListAsync())
            .ToDictionary(d => d.Id);

        var items = page.Items.Select(i =>
        {
            versionMap.TryGetValue(i.DefinitionVersionId, out var ver);
            var defName = "";
            var defId = 0L;
            var verNo = 0;
            if (ver is not null)
            {
                defId = ver.DefinitionId;
                verNo = ver.Version;
                if (defMap.TryGetValue(ver.DefinitionId, out var def))
                    defName = def.Name;
            }

            return new WfInstanceListItemOutput
            {
                Id = i.Id,
                DefinitionId = defId,
                DefinitionName = defName,
                Version = verNo,
                BusinessKey = i.BusinessKey,
                Status = i.Status,
                VariablesJson = i.VariablesJson,
                CreateTime = i.CreateTime,
            };
        }).ToList();

        return new PagedList<WfInstanceListItemOutput>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = items,
        };
    }

    /// <summary>查当前用户在本实例上的 Pending 审批待办(至多一条活跃链)。</summary>
    protected virtual async Task<WfTodoItemOutput?> FindMyPendingAsync(
        long instanceId,
        long userId,
        string definitionName,
        long definitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = await instances.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId)
            .FirstAsync();
        if (instance is null)
            return null;

        var activeTasks = await tasks.AsQueryable()
            .Where(t => t.InstanceId == instanceId)
            .ToListAsync();
        if (activeTasks.Count == 0)
            return null;

        var taskIds = activeTasks.Select(t => t.Id).ToList();
        var actor = await actors.AsQueryable()
            .Where(a => taskIds.Contains(a.TaskId)
                        && a.UserId == userId
                        && a.ActorType == WfActorType.Approver
                        && a.Status == WfActorStatus.Pending)
            .OrderBy(a => a.Id, OrderByType.Asc)
            .FirstAsync();
        if (actor is null)
            return null;

        var task = activeTasks.First(t => t.Id == actor.TaskId);
        var nodeName = await ResolveNodeName(instance.DefinitionVersionId, task.NodeId);

        return new WfTodoItemOutput
        {
            TaskId = task.Id,
            ActorId = actor.Id,
            InstanceId = instanceId,
            NodeId = task.NodeId,
            NodeName = nodeName,
            SignMode = task.SignMode,
            DueTime = task.DueTime,
            DefinitionId = definitionId,
            DefinitionName = definitionName,
            BusinessKey = instance.BusinessKey,
            StarterUserId = instance.StarterUserId,
            VariablesJson = instance.VariablesJson,
            CreateTime = task.CreateTime,
        };
    }

    protected virtual async Task<string?> ResolveNodeName(long definitionVersionId, string nodeId)
    {
        var version = await versions.AsQueryable()
            .Where(v => v.Id == definitionVersionId)
            .FirstAsync();
        if (version is null)
            return null;
        var model = WfModelJson.Deserialize(version.ModelJson);
        if (model is null)
            return null;
        for (var n = model.Root; n is not null; n = n.Next)
        {
            if (n.Id == nodeId)
                return n.Name;
        }

        return null;
    }

    protected virtual async Task<(WfDefinition Definition, WfDefinitionVersion Version, WfModel Model)>
        RequireStartableAsync(
            long definitionId,
            long userId,
            long? orgId,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (definitionId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);
        if (userId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.StarterInvalid);

        var def = await definitions.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(d => d.Id == definitionId)
            .FirstAsync()
                  ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);
        if (def.Status != WfDefinitionStatus.Published || def.CurrentVersion < 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionStatusConflict,
                new Dictionary<string, object?>
                {
                    ["status"] = def.Status.ToString(),
                    ["currentVersion"] = def.CurrentVersion,
                    ["reason"] = "notPublished",
                });
        }

        var version = await versions.AsQueryable()
            .Where(v => v.DefinitionId == def.Id && v.Version == def.CurrentVersion)
            .FirstAsync()
                      ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);
        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);
        var roleIds = await GetRoleIdsAsync(userId, cancellationToken);
        if (!CanStart(model, userId, orgId, roleIds))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InitiatorNotAllowed,
                new Dictionary<string, object?> { ["definitionId"] = definitionId });
        }

        return (def, version, model);
    }

    protected virtual async Task<HashSet<long>> GetRoleIdsAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = await userRoles.Db.Queryable<SysUserRole>()
            .InnerJoin<SysRole>((ur, r) => ur.RoleId == r.Id && r.Enabled)
            .Where((ur, r) => ur.UserId == userId)
            .Select((ur, r) => ur.RoleId)
            .ToListAsync();
        return ids.ToHashSet();
    }

    protected virtual bool CanStart(
        WfModel model,
        long userId,
        long? orgId,
        IReadOnlySet<long> roleIds)
    {
        var scope = model.Root.Props?.InitiatorScope;
        if (scope is null || scope.Count == 0)
            return true;

        return scope.Any(item =>
            item.Id > 0
            && (item.Type.Trim().ToLowerInvariant() switch
            {
                "user" => item.Id == userId,
                "role" => roleIds.Contains(item.Id),
                "org" => orgId == item.Id,
                _ => false,
            }));
    }

    protected virtual async Task EnsureParticipantAsync(
        WfInstance instance,
        long userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceAccessDenied);
        if (instance.StarterUserId == userId)
            return;

        var taskIds = await tasks.AsQueryable()
            .Where(t => t.InstanceId == instance.Id)
            .Select(t => t.Id)
            .ToListAsync();
        if (taskIds.Count > 0 && await actors.AsQueryable()
                .AnyAsync(a => taskIds.Contains(a.TaskId)
                               && a.UserId == userId
                               && a.ActorType == WfActorType.Approver
                               && a.Status == WfActorStatus.Pending))
            return;
        if (await hisTasks.AnyAsync(h => h.InstanceId == instance.Id && h.UserId == userId))
            return;
        if (await ccs.AnyAsync(c => c.InstanceId == instance.Id && c.UserId == userId))
            return;

        throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceAccessDenied,
            new Dictionary<string, object?> { ["instanceId"] = instance.Id });
    }

    protected virtual WfHisTaskOutput MapHisTask(WfHisTask h) => new()
    {
        Id = h.Id,
        NodeId = h.NodeId,
        NodeName = h.NodeName,
        UserId = h.UserId,
        Action = h.Action,
        Comment = h.Comment,
        TransferToUserId = h.TransferToUserId,
        DurationMs = h.DurationMs,
        CreateTime = h.CreateTime,
    };
}
