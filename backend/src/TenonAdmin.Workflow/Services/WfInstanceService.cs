using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    IRepository<WfAiDecision> aiDecisions,
    IRepository<SysUserRole> userRoles,
    IWorkflowEngine engine,
    ICurrentUser? currentUser = null,
    IPermissionProvider? permissions = null,
    TimeProvider? timeProvider = null,
    ILogger<WfInstanceService>? logger = null) : IWfInstanceService, IWfAiDecisionAuditReader
{
    private const int MaximumAiAuditJsonCharacters = AiDecisionProposalParser.MaximumJsonCharacters;
    private static readonly HashSet<string> HistoryPayloadFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "action", "toUserId", "targetNodeId", "fromNodeId", "comment", "payloadHash",
        "eventName", "forkId", "armId", "armName", "isDefault", "parentTokenId", "childTokenId",
        "parentNodeVisitId", "childEntryNodeVisitId", "reason", "status", "arms",
        "nodeId", "userIds",
    };

    protected sealed record CurrentTaskSnapshot(long TaskId, long TokenId, long? NodeVisitId, string NodeId);

    /// <summary>保留 M3b-0 前的构造签名，供继承内置服务的消费者继续编译。</summary>
    public WfInstanceService(
        IRepository<WfInstance> instances,
        IRepository<WfDefinition> definitions,
        IRepository<WfDefinitionVersion> versions,
        IRepository<WfHistory> histories,
        IRepository<WfHisTask> hisTasks,
        IRepository<WfTask> tasks,
        IRepository<WfTaskActor> actors,
        IRepository<WfCc> ccs,
        IRepository<SysUserRole> userRoles,
        IWorkflowEngine engine,
        ICurrentUser? currentUser = null,
        IPermissionProvider? permissions = null)
        : this(
            instances, definitions, versions, histories, hisTasks, tasks, actors, ccs,
            null!, userRoles, engine, currentUser, permissions, null, null)
    {
    }

    /// <summary>监控列表权限码 = 规范化路由,与 <c>[RolePermission]</c> 同一套。</summary>
    public const string MonitorPermission = "GET:/api/v1/workflow/instance/monitor";

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
            Model = WfRuntimeModelProjector.Project(model)!,
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
                AllowAnyFileOwner = currentUser?.IsSuperAdmin == true,
                SelectedUserIdsByNode = input.SelectedUserIdsByNode,
                RequestId = input.RequestId,
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
    public virtual async Task<PagedList<WfInstanceListItemOutput>> PageMonitorAsync(
        WfInstanceMonitorPageInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        List<long>? actorInstanceIds = null;
        if (input.ActorUserId is > 0)
        {
            var pendingTaskIds = await actors.AsQueryable()
                .Where(a => a.UserId == input.ActorUserId.Value
                            && a.ActorType == WfActorType.Approver
                            && a.Status == WfActorStatus.Pending)
                .Select(a => a.TaskId)
                .ToListAsync();
            var pendingIds = pendingTaskIds.Count == 0
                ? []
                : await tasks.AsQueryable()
                    .Where(t => pendingTaskIds.Contains(t.Id))
                    .Select(t => t.InstanceId)
                    .ToListAsync();
            var hisIds = await hisTasks.AsQueryable()
                .Where(h => h.UserId == input.ActorUserId.Value)
                .Select(h => h.InstanceId)
                .ToListAsync();
            actorInstanceIds = pendingIds.Union(hisIds).Distinct().ToList();
            if (actorInstanceIds.Count == 0)
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

        List<long>? ccInstanceIds = null;
        if (input.CcUserId is > 0)
        {
            ccInstanceIds = await ccs.AsQueryable()
                .Where(c => c.UserId == input.CcUserId.Value)
                .Select(c => c.InstanceId)
                .Distinct()
                .ToListAsync();
            if (ccInstanceIds.Count == 0)
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

        // 不 ClearFilter<IOrgScoped>:监控叠在现有机构范围上,参与条件是业务谓词。
        var page = await instances.AsQueryable()
            .WhereIF(input.StarterUserId is > 0, i => i.StarterUserId == input.StarterUserId!.Value)
            .WhereIF(actorInstanceIds is not null, i => actorInstanceIds!.Contains(i.Id))
            .WhereIF(ccInstanceIds is not null, i => ccInstanceIds!.Contains(i.Id))
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
        await MarkMyCcReadAsync(instance.Id, currentUserId, cancellationToken);

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
        var modelIndex = model is null ? null : WfModelIndex.Build(model);

        var his = await hisTasks.AsQueryable()
            .Where(h => h.InstanceId == instance.Id)
            .OrderBy(h => h.CreateTime, OrderByType.Asc)
            .ToListAsync();

        var events = await histories.AsQueryable()
            .Where(h => h.InstanceId == instance.Id)
            .OrderBy(h => h.Sequence, OrderByType.Asc)
            .OrderBy(h => h.CreateTime, OrderByType.Asc)
            .OrderBy(h => h.Id, OrderByType.Asc)
            .ToListAsync();

        var currentTasks = await tasks.AsQueryable()
            .Where(t => t.InstanceId == instance.Id)
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.NodeId, t.TokenId, t.NodeVisitId })
            .ToListAsync();
        var runtimeTokens = await instances.Db.Queryable<WfToken>()
            .Where(t => t.InstanceId == instance.Id)
            .OrderBy(t => t.Id)
            .ToListAsync();
        var runtimeTokenIds = runtimeTokens.Select(t => t.Id).ToList();
        var runtimeArms = runtimeTokenIds.Count == 0
            ? new List<WfParallelArm>()
            : await instances.Db.Queryable<WfParallelArm>()
                .Where(arm => runtimeTokenIds.Contains(arm.ParentTokenId))
                .OrderBy(arm => arm.ForkId, OrderByType.Asc)
                .OrderBy(arm => arm.ArmId, OrderByType.Asc)
                .ToListAsync();
        var currentNodeIds = currentTasks
            .Select(t => t.NodeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        long? currentTaskId = currentTasks.Count > 0 ? currentTasks[0].Id : null;

        IReadOnlyList<WfTodoItemOutput> myPendingTasks = [];
        if (currentUserId > 0 && instance.Status == WfInstanceStatus.Running)
        {
            myPendingTasks = await FindMyPendingTasksAsync(
                instance,
                currentUserId,
                def?.Name ?? "",
                version.DefinitionId,
                model,
                cancellationToken);
        }
        var viewerHistoryNodeIds = his
            .Where(item => item.UserId == currentUserId)
            .Select(item => item.NodeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var permissionNodeIds = myPendingTasks.Count > 0
            ? myPendingTasks
                .Select(task => task.NodeId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList()
            : viewerHistoryNodeIds.Count > 0
                ? viewerHistoryNodeIds
                : currentNodeIds.Count > 0
                    ? currentNodeIds
                    : his.Select(item => item.NodeId).ToList();
        var viewPermissions = ResolveFormPermissions(model, permissionNodeIds);
        var myPending = myPendingTasks.FirstOrDefault();
        var currentTaskSnapshots = currentTasks
            .Select(task => new CurrentTaskSnapshot(task.Id, task.TokenId, task.NodeVisitId, task.NodeId))
            .ToList();
        var myTakeBackTaskId = currentUserId > 0
            ? await FindMyTakeBackTaskIdAsync(
                currentUserId,
                his,
                currentTaskSnapshots,
                model,
                instance.Id,
                cancellationToken)
            : null;

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
            VariablesJson = WfFormRuntime.ProjectVisibleJson(model?.FormSchema, instance.VariablesJson, viewPermissions),
            FormComponent = model?.FormComponent,
            CreateTime = instance.CreateTime,
            MyPendingTask = myPending,
            MyPendingTasks = myPendingTasks,
            HisTasks = his.Select(MapHisTask).ToList(),
            Model = WfRuntimeModelProjector.Project(model),
            VisitedNodeIds = CollectVisitedNodeIds(events),
            CurrentNodeIds = currentNodeIds,
            CurrentTaskId = currentTaskId,
            CurrentTasks = currentTaskSnapshots.Select(task => new WfCurrentTaskOutput
            {
                TaskId = task.TaskId,
                TokenId = task.TokenId,
                NodeVisitId = task.NodeVisitId,
                NodeId = task.NodeId,
                NodeName = modelIndex?.Find(task.NodeId)?.Name,
            }).ToList(),
            ParallelForks = MapParallelForks(model, runtimeTokens, runtimeArms, events),
            MyTakeBackTaskId = myTakeBackTaskId,
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
            .OrderBy(h => h.Sequence, OrderByType.Asc)
            .OrderBy(h => h.CreateTime, OrderByType.Asc)
            .OrderBy(h => h.Id, OrderByType.Asc)
            .ToListAsync();

        return list.Select(h => new WfHistoryItemOutput
        {
            Id = h.Id,
            Sequence = h.Sequence,
            EventType = h.EventType,
            NodeId = h.NodeId,
            TokenId = h.TokenId,
            NodeVisitId = h.NodeVisitId,
            PayloadJson = ProjectHistoryPayload(h.Id, h.PayloadJson),
            CreateTime = h.CreateTime,
        }).ToList();
    }

    private string? ProjectHistoryPayload(long historyId, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var projected = ProjectHistoryObject(document.RootElement);
            return projected.Count == 0 ? null : JsonSerializer.Serialize(projected, WfModelJson.Options);
        }
        catch (JsonException)
        {
            logger?.LogWarning("工作流历史载荷损坏，已隐藏。HistoryId={HistoryId}", historyId);
            return null;
        }
    }

    private static Dictionary<string, JsonElement> ProjectHistoryObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return [];

        var projected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!HistoryPayloadFields.Contains(property.Name)) continue;
            if (property.NameEquals("arms"))
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;
                var arms = property.Value.EnumerateArray()
                    .Select(ProjectHistoryObject)
                    .Where(arm => arm.Count > 0)
                    .ToArray();
                if (arms.Length > 0)
                    projected[property.Name] = JsonSerializer.SerializeToElement(arms, WfModelJson.Options);
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                if (IsPrimitiveHistoryArray(property.Value))
                    projected[property.Name] = property.Value;
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
                projected[property.Name] = property.Value;
        }
        return projected;
    }

    /// <summary>允许 <c>userIds</c> 这类雪花 Id 数组;嵌套对象/数组仍拒绝,避免把内部结构透出。</summary>
    private static bool IsPrimitiveHistoryArray(JsonElement array) =>
        array.EnumerateArray().All(item => item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array));

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<WfAiDecisionAuditOutput>> ListAiDecisionsAsync(
        long instanceId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (aiDecisions is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        var instance = await RequireInstanceAsync(instanceId);
        await EnsureParticipantAsync(instance, currentUserId, cancellationToken);

        var rows = await aiDecisions.AsQueryable()
            .Where(a => a.InstanceId == instanceId)
            .OrderBy(a => a.CreateTime, OrderByType.Asc)
            .OrderBy(a => a.Id, OrderByType.Asc)
            .ToListAsync();

        return rows.Select(MapAiDecisionAudit).ToList();
    }

    /// <inheritdoc />
    public virtual Task<WfEngineResult> CancelAsync(
        long instanceId,
        long callerUserId,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(
            new CancelInstanceCmd
            {
                InstanceId = instanceId,
                CallerUserId = callerUserId,
                RequestId = requestId,
            },
            cancellationToken);

    /// <inheritdoc />
    public virtual Task<WfEngineResult> ResubmitAsync(
        long instanceId,
        long callerUserId,
        string? variablesJson,
        IReadOnlyDictionary<string, List<long>>? selectedUserIdsByNode,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(
            new ResubmitInstanceCmd
            {
                InstanceId = instanceId,
                CallerUserId = callerUserId,
                VariablesJson = variablesJson,
                SelectedUserIdsByNode = selectedUserIdsByNode,
                AllowAnyFileOwner = currentUser?.IsSuperAdmin == true,
                RequestId = requestId,
            },
            cancellationToken);

    /// <summary>
    /// 查看详情即已读:把当前用户在本实例上未读的 <c>wf_cc</c> 行翻掉。
    /// 无抄送行时是空操作;已读幂等。对齐语义契约「抄送已读由查看详情页标记」。
    /// </summary>
    protected virtual async Task MarkMyCcReadAsync(
        long instanceId,
        long userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId <= 0) return;

        var unread = await ccs.AsQueryable()
            .Where(c => c.InstanceId == instanceId && c.UserId == userId && c.IsRead == false)
            .ToListAsync();
        if (unread.Count == 0) return;

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        foreach (var row in unread)
        {
            row.IsRead = true;
            row.ReadTime = now;
            await ccs.UpdateAsync(row);
        }
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
                VariablesJson = ver is not null && WfModelJson.Deserialize(ver.ModelJson)?.FormSchema is null
                    ? i.VariablesJson
                    : null,
                StarterUserId = i.StarterUserId,
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
        var instance = await instances.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId)
            .FirstAsync();
        if (instance is null)
            return null;
        var version = await versions.AsQueryable()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync(cancellationToken);
        var model = version is null ? null : WfModelJson.Deserialize(version.ModelJson);
        return (await FindMyPendingTasksAsync(
            instance, userId, definitionName, definitionId, model, cancellationToken)).FirstOrDefault();
    }

    /// <summary>查当前用户在本实例上的全部 Pending 审批待办，按 actor Id 稳定排序。</summary>
    protected virtual async Task<IReadOnlyList<WfTodoItemOutput>> FindMyPendingTasksAsync(
        WfInstance instance,
        long userId,
        string definitionName,
        long definitionId,
        WfModel? model,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activeTasks = await tasks.AsQueryable()
            .Where(t => t.InstanceId == instance.Id)
            .ToListAsync(cancellationToken);
        if (activeTasks.Count == 0)
            return [];

        var taskMap = activeTasks.ToDictionary(task => task.Id);
        var taskIds = activeTasks.Select(task => task.Id).ToList();
        var pendingActors = await actors.AsQueryable()
            .Where(a => taskIds.Contains(a.TaskId)
                        && a.UserId == userId
                        && a.ActorType == WfActorType.Approver
                        && a.Status == WfActorStatus.Pending)
            .OrderBy(a => a.Id, OrderByType.Asc)
            .ToListAsync(cancellationToken);
        var modelIndex = model is null ? null : WfModelIndex.Build(model);

        return pendingActors
            .Where(actor => taskMap.ContainsKey(actor.TaskId))
            .Select(actor =>
            {
                var task = taskMap[actor.TaskId];
                return new WfTodoItemOutput
                {
                    TaskId = task.Id,
                    ActorId = actor.Id,
                    InstanceId = instance.Id,
                    NodeId = task.NodeId,
                    NodeName = modelIndex?.Find(task.NodeId)?.Name,
                    SignMode = task.SignMode,
                    DueTime = task.DueTime,
                    DefinitionId = definitionId,
                    DefinitionName = definitionName,
                    BusinessKey = instance.BusinessKey,
                    StarterUserId = instance.StarterUserId,
                    VariablesJson = WfFormRuntime.ProjectVisibleJson(
                        model?.FormSchema,
                        instance.VariablesJson,
                        ResolveFormPermissions(model, [task.NodeId])),
                    CreateTime = task.CreateTime,
                };
            })
            .ToList();
    }

    protected virtual async Task<long?> FindMyTakeBackTaskIdAsync(
        long userId,
        IReadOnlyList<WfHisTask> history,
        IReadOnlyList<CurrentTaskSnapshot> currentTasks,
        WfModel? model,
        long instanceId,
        CancellationToken cancellationToken)
    {
        var index = model is null ? null : WfModelIndex.Build(model);
        foreach (var task in currentTasks)
        {
            var token = await instances.Db.Queryable<WfToken>()
                .Where(token => token.Id == task.TokenId && token.InstanceId == instanceId)
                .FirstAsync(cancellationToken);
            if (token is null || token.Status != WfTokenStatus.Active
                || token.ParentTokenId is not null || token.ForkId is not null)
                continue;

            var approval = history
                .Where(item => item.TokenId == task.TokenId
                               && item.UserId == userId
                               && item.Action == WfTaskAction.Approve)
                .OrderByDescending(item => item.Id)
                .FirstOrDefault();
            if (approval?.NodeVisitId is not { } approvalVisitId
                || approvalVisitId == task.NodeVisitId
                || index?.Find(approval.NodeId)?.Type != WfNodeType.Approval)
                continue;
            if (history.Any(item => item.TokenId == task.TokenId && item.Id > approval.Id))
                continue;

            var executions = await instances.Db.Queryable<WfNodeExecution>()
                .Where(execution => execution.InstanceId == instanceId
                                    && execution.TokenId == task.TokenId
                                    && execution.NodeVisitId != approvalVisitId
                                    && execution.CreateTime >= approval.CreateTime)
                .Select(execution => new { execution.Id, execution.Status })
                .ToListAsync(cancellationToken);
            if (executions.Any(execution => execution.Status is WfNodeExecutionStatus.Succeeded
                or WfNodeExecutionStatus.ManualFallback
                or WfNodeExecutionStatus.Cancelled
                or WfNodeExecutionStatus.Failed))
                continue;

            var executionIds = executions.Select(execution => execution.Id).ToList();
            if (executionIds.Count > 0 && await instances.Db.Queryable<WfOutbox>()
                    .Where(outbox => executionIds.Contains(outbox.ExecutionId)
                                     && (outbox.Status == WfOutboxStatus.Dispatched
                                         || outbox.Status == WfOutboxStatus.Failed))
                    .AnyAsync(cancellationToken))
                continue;

            return task.TaskId;
        }
        return null;
    }

    private static IReadOnlyList<WfFormFieldPerm> ResolveFormPermissions(
        WfModel? model,
        IEnumerable<string> nodeIds)
    {
        if (model?.FormSchema is null) return [];
        var index = WfModelIndex.Build(model);
        return nodeIds
            .Select(index.Find)
            .Where(node => node?.Type == WfNodeType.Approval)
            .SelectMany(node => node!.Props?.FormPerms ?? [])
            .GroupBy(permission => permission.Field, StringComparer.Ordinal)
            .Select(group => group.OrderBy(permission => permission.Access).First())
            .ToList();
    }

    /// <summary>按 <see cref="WfModelIndex"/>(含分支臂内节点)解析节点名——不写第三次主链线性扫描。</summary>
    protected virtual async Task<string?> ResolveNodeName(long definitionVersionId, string nodeId)
    {
        var version = await versions.AsQueryable()
            .Where(v => v.Id == definitionVersionId)
            .FirstAsync();
        if (version is null)
            return null;
        var model = WfModelJson.Deserialize(version.ModelJson);
        return model is null ? null : WfModelIndex.Build(model).Find(nodeId)?.Name;
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

    /// <summary>
    /// 流程图回放:取最后一次向后跳转之后的 <see cref="WfHistoryEventType.NodeEnter"/> 节点。
    /// 重提/拒绝/退回会让同一节点反复进出;丢掉 cutoff 会把回退前旧路径一起点亮。
    /// </summary>
    protected virtual IReadOnlyList<string> CollectVisitedNodeIds(IReadOnlyList<WfHistory> events)
    {
        var cutoff = -1;
        for (var i = 0; i < events.Count; i++)
        {
            var t = events[i].EventType;
            if (t is WfHistoryEventType.RejectRouted
                or WfHistoryEventType.TaskReturned
                or WfHistoryEventType.InstanceResubmitted)
            {
                cutoff = i;
            }
        }

        var visited = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = cutoff + 1; i < events.Count; i++)
        {
            if (events[i].EventType != WfHistoryEventType.NodeEnter) continue;
            var nodeId = events[i].NodeId;
            if (string.IsNullOrWhiteSpace(nodeId) || !seen.Add(nodeId)) continue;
            visited.Add(nodeId);
        }

        return visited;
    }

    /// <summary>
    /// 超管,或持有监控列表权限的管理员,可以打开非自己参与的实例详情。
    /// 不把 GET <c>instance/{id}</c> 改成 RolePermission——参与人无监控码也要能看自己的单。
    /// </summary>
    protected virtual async Task<bool> CanMonitorInstancesAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId <= 0) return false;
        if (currentUser is { IsSuperAdmin: true, UserId: { } me } && me == userId)
            return true;
        if (permissions is null) return false;
        var codes = await permissions.GetPermissionCodesAsync(userId, cancellationToken);
        return codes.Contains(MonitorPermission);
    }

    protected virtual async Task EnsureParticipantAsync(
        WfInstance instance,
        long userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceAccessDenied);
        if (await CanMonitorInstancesAsync(userId, cancellationToken))
        {
            if (await instances.AsQueryable().Where(i => i.Id == instance.Id).AnyAsync())
                return;
        }
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
        OriginalUserId = h.OriginalUserId,
        DelegationRuleId = h.DelegationRuleId,
        DelegationScopeOrgId = h.DelegationScopeOrgId,
        TransferToUserId = h.TransferToUserId,
        TargetUserId = h.TargetUserId,
        DurationMs = h.DurationMs,
        CreateTime = h.CreateTime,
    };

    protected virtual IReadOnlyList<WfParallelForkOutput> MapParallelForks(
        WfModel? model,
        IReadOnlyList<WfToken> runtimeTokens,
        IReadOnlyList<WfParallelArm> runtimeArms,
        IReadOnlyList<WfHistory> events)
    {
        if (runtimeArms.Count == 0) return [];

        var tokenMap = runtimeTokens.ToDictionary(token => token.Id);
        var forkNodeIds = new Dictionary<long, string?>();
        foreach (var item in events.Where(item => item.EventType == WfHistoryEventType.ParallelFork))
        {
            if (string.IsNullOrWhiteSpace(item.PayloadJson)
                || !TryReadForkId(item.PayloadJson, out var forkId))
                continue;
            forkNodeIds[forkId] = item.NodeId;
        }

        var index = model is null ? null : WfModelIndex.Build(model);
        return runtimeArms
            .GroupBy(arm => arm.ForkId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var rows = group.ToList();
                var parent = tokenMap.GetValueOrDefault(rows[0].ParentTokenId);
                var status = rows.Any(row => row.Status == WfParallelArmStatus.Active)
                    ? WfParallelForkStatus.Waiting
                    : rows.All(row => row.Status == WfParallelArmStatus.Completed)
                        ? WfParallelForkStatus.Joined
                        : WfParallelForkStatus.Cancelled;
                var isCurrentFork = parent?.ForkId == group.Key;
                var activeArmCount = rows.Count(row => row.Status == WfParallelArmStatus.Active);
                var nodeId = forkNodeIds.GetValueOrDefault(group.Key);
                var node = nodeId is null ? null : index?.Find(nodeId);
                return new WfParallelForkOutput
                {
                    ForkId = group.Key,
                    ParentTokenId = rows[0].ParentTokenId,
                    ParentNodeVisitId = rows[0].ParentNodeVisitId,
                    NodeId = nodeId,
                    NodeName = node?.Name,
                    // 父 token 会被下一次重提复用；旧 fork 不能读取它当前代次的状态。
                    ParentTokenStatus = isCurrentFork
                        ? parent!.Status
                        : status switch
                        {
                            WfParallelForkStatus.Waiting => WfTokenStatus.WaitingJoin,
                            WfParallelForkStatus.Joined => WfTokenStatus.Active,
                            _ => WfTokenStatus.Cancelled,
                        },
                    PendingArmCount = isCurrentFork
                        ? parent!.PendingArmCount ?? activeArmCount
                        : activeArmCount,
                    Status = status,
                    Arms = rows.Select(row =>
                    {
                        var child = row.ChildTokenId is { } childId
                            ? tokenMap.GetValueOrDefault(childId)
                            : null;
                        var childNode = child is null ? null : index?.Find(child.NodeId);
                        return new WfParallelArmOutput
                        {
                            ForkId = row.ForkId,
                            ArmId = row.ArmId,
                            ParentTokenId = row.ParentTokenId,
                            ChildTokenId = row.ChildTokenId,
                            Status = row.Status,
                            ParentNodeVisitId = row.ParentNodeVisitId,
                            ChildEntryNodeVisitId = row.ChildEntryNodeVisitId,
                            CurrentNodeId = child?.NodeId,
                            CurrentNodeName = childNode?.Name,
                            ChildTokenStatus = child?.Status,
                            Reason = row.Reason,
                        };
                    }).ToList(),
                };
            })
            .ToList();
    }

    private static bool TryReadForkId(string payloadJson, out long forkId)
    {
        forkId = 0;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty("forkId", out var value)
                   && value.TryGetInt64(out forkId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    protected virtual WfAiDecisionAuditOutput MapAiDecisionAudit(WfAiDecision row) => new()
    {
        Id = row.Id,
        NodeId = row.NodeId,
        AttemptNo = row.AttemptNo,
        ProviderResultType = row.ProviderResultType,
        Provider = ReadOptionalHash(row.Provider),
        Model = ReadOptionalHash(row.Model),
        InputHash = ReadOptionalHash(row.InputHash),
        PromptVersion = ReadOptionalHash(row.PromptVersion),
        ProposalSchemaVersion = row.ProposalSchemaVersion,
        SchemaValid = row.SchemaValid,
        PolicyVersion = ReadOptionalHash(row.PolicyVersion),
        PolicyClassification = row.PolicyClassification,
        Recommendation = row.Recommendation,
        Confidence = row.Confidence,
        RiskFlags = ReadStringArray(row.RiskFlagsJson),
        EvidenceRefs = ReadEvidenceRefs(row.EvidenceRefsJson),
        LatencyMilliseconds = row.LatencyMilliseconds,
        PromptTokens = row.PromptTokens,
        CompletionTokens = row.CompletionTokens,
        TotalTokens = row.TotalTokens,
        FallbackReason = row.FallbackReason,
        ShadowMode = row.ShadowMode,
        CreateTime = row.CreateTime,
    };

    private static IReadOnlyList<string> ReadStringArray(string? json)
    {
        if (json is null) return [];
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumAiAuditJsonCharacters)
            throw AuditIntegrityException();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw AuditIntegrityException();

            var values = document.RootElement.EnumerateArray().ToArray();
            if (values.Length > AiDecisionProposalParser.MaximumRiskFlagCount
                || values.Any(item => item.ValueKind != JsonValueKind.String
                                      || !AiDecisionEvidence.IsValidContentHash(item.GetString())))
                throw AuditIntegrityException();

            var result = values.Select(item => item.GetString()!).ToArray();
            if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
                throw AuditIntegrityException();
            return result;
        }
        catch (JsonException)
        {
            throw AuditIntegrityException();
        }
    }

    private static IReadOnlyList<WfAiDecisionEvidenceRefOutput> ReadEvidenceRefs(string? json)
    {
        if (json is null) return [];
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumAiAuditJsonCharacters)
            throw AuditIntegrityException();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw AuditIntegrityException();

            var items = document.RootElement.EnumerateArray().ToArray();
            if (items.Length > AiDecisionProposalParser.MaximumEvidenceCount
                || items.Any(item => !IsValidEvidenceRef(item)))
            {
                throw AuditIntegrityException();
            }

            var result = items
                .Select(item => new WfAiDecisionEvidenceRefOutput
                {
                    Id = item.GetProperty("id").GetString()!,
                    Source = item.GetProperty("source").GetString()!,
                    ContentHash = item.GetProperty("contentHash").GetString()!,
                })
                .ToArray();
            if (result.Select(item => (item.Id, item.Source, item.ContentHash)).Distinct().Count() != result.Length)
                throw AuditIntegrityException();
            return result;
        }
        catch (JsonException)
        {
            throw AuditIntegrityException();
        }
    }

    private static bool IsValidEvidenceRef(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return false;

        var properties = item.EnumerateObject().ToArray();
        if (properties.Length != 3
            || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 3
            || !item.TryGetProperty("id", out var id)
            || !item.TryGetProperty("source", out var source)
            || !item.TryGetProperty("contentHash", out var hash)
            || id.ValueKind != JsonValueKind.String
            || source.ValueKind != JsonValueKind.String
            || hash.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return AiDecisionEvidence.IsValidContentHash(id.GetString())
               && AiDecisionEvidence.IsValidContentHash(source.GetString())
               && AiDecisionEvidence.IsValidContentHash(hash.GetString());
    }

    private static string? ReadOptionalHash(string? value)
    {
        if (value is not null && !AiDecisionEvidence.IsValidContentHash(value))
            throw AuditIntegrityException();
        return value;
    }

    private static AdminException AuditIntegrityException() =>
        WorkflowErrorCode.Exception(
            WorkflowErrorCode.OperationFailed,
            new Dictionary<string, object?> { ["reason"] = "aiAuditCorrupt" });
}
