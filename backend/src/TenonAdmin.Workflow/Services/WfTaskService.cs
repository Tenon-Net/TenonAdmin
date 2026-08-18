using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置审批任务服务:待办/已办列表 + 三动词派发引擎 Cmd。
/// </summary>
public class WfTaskService(
    IWorkflowEngine engine,
    IRepository<WfTaskActor> actors,
    IRepository<WfTask> tasks,
    IRepository<WfHisTask> hisTasks,
    IRepository<WfInstance> instances,
    IRepository<WfDefinition> definitions,
    IRepository<WfDefinitionVersion> versions) : IWfTaskService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<WfTodoItemOutput>> PageTodoAsync(
        long userId,
        WfTaskPageInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scopedTaskIds = await ResolveTaskIdsByDefinitionAsync(input.DefinitionId, cancellationToken);
        if (input.DefinitionId is > 0 && scopedTaskIds is { Count: 0 })
            return EmptyTodoPage(input);

        var actorPage = await actors.AsQueryable()
            .Where(a => a.UserId == userId
                        && a.ActorType == WfActorType.Approver
                        && a.Status == WfActorStatus.Pending)
            .WhereIF(scopedTaskIds is not null, a => scopedTaskIds!.Contains(a.TaskId))
            .ToPagedListAsync(input, q => q.OrderBy(a => a.Id, OrderByType.Desc));

        if (actorPage.Items.Count == 0)
        {
            return new PagedList<WfTodoItemOutput>
            {
                Current = actorPage.Current,
                Size = actorPage.Size,
                Total = actorPage.Total,
                Items = [],
            };
        }

        var taskIds = actorPage.Items.Select(a => a.TaskId).Distinct().ToList();
        var taskMap = (await tasks.AsQueryable().Where(t => taskIds.Contains(t.Id)).ToListAsync())
            .ToDictionary(t => t.Id);
        var instanceIds = taskMap.Values.Select(t => t.InstanceId).Distinct().ToList();
        var instanceMap = (await instances.AsQueryable()
                .ClearFilter<IOrgScoped>()
                .Where(i => instanceIds.Contains(i.Id))
                .ToListAsync())
            .ToDictionary(i => i.Id);
        var versionIds = instanceMap.Values.Select(i => i.DefinitionVersionId).Distinct().ToList();
        var versionMap = (await versions.AsQueryable().Where(v => versionIds.Contains(v.Id)).ToListAsync())
            .ToDictionary(v => v.Id);
        var defIds = versionMap.Values.Select(v => v.DefinitionId).Distinct().ToList();
        var defMap = (await definitions.AsQueryable()
                .ClearFilter<IOrgScoped>()
                .Where(d => defIds.Contains(d.Id))
                .ToListAsync())
            .ToDictionary(d => d.Id);
        var modelCache = new Dictionary<long, WfModel?>();

        var items = new List<WfTodoItemOutput>(actorPage.Items.Count);
        foreach (var actor in actorPage.Items)
        {
            if (!taskMap.TryGetValue(actor.TaskId, out var task))
                continue;
            if (!instanceMap.TryGetValue(task.InstanceId, out var instance))
                continue;

            versionMap.TryGetValue(instance.DefinitionVersionId, out var ver);
            var defId = ver?.DefinitionId ?? 0;
            var defName = defId > 0 && defMap.TryGetValue(defId, out var def) ? def.Name : "";

            items.Add(new WfTodoItemOutput
            {
                TaskId = task.Id,
                ActorId = actor.Id,
                InstanceId = instance.Id,
                NodeId = task.NodeId,
                NodeName = ResolveNodeNameCached(instance.DefinitionVersionId, task.NodeId, versionMap, modelCache),
                SignMode = task.SignMode,
                DueTime = task.DueTime,
                DefinitionId = defId,
                DefinitionName = defName,
                BusinessKey = instance.BusinessKey,
                StarterUserId = instance.StarterUserId,
                VariablesJson = instance.VariablesJson,
                CreateTime = task.CreateTime,
            });
        }

        return new PagedList<WfTodoItemOutput>
        {
            Current = actorPage.Current,
            Size = actorPage.Size,
            Total = actorPage.Total,
            Items = items,
        };
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<WfDoneItemOutput>> PageDoneAsync(
        long userId,
        WfTaskPageInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scopedInstanceIds = await ResolveInstanceIdsByDefinitionAsync(input.DefinitionId, cancellationToken);
        if (input.DefinitionId is > 0 && scopedInstanceIds is { Count: 0 })
            return EmptyDonePage(input);

        var page = await hisTasks.AsQueryable()
            .Where(h => h.UserId == userId)
            .WhereIF(scopedInstanceIds is not null, h => scopedInstanceIds!.Contains(h.InstanceId))
            .ToPagedListAsync(input, q => q.OrderBy(h => h.Id, OrderByType.Desc));

        return await MapDonePageAsync(page, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<WfEngineResult> ApproveAsync(
        long taskId,
        long userId,
        string? comment = null,
        CancellationToken cancellationToken = default)
        => CompleteAsync(taskId, userId, WfTaskAction.Approve, comment, cancellationToken);

    /// <inheritdoc />
    public virtual Task<WfEngineResult> RejectAsync(
        long taskId,
        long userId,
        string? comment = null,
        CancellationToken cancellationToken = default)
        => CompleteAsync(taskId, userId, WfTaskAction.Reject, comment, cancellationToken);

    /// <inheritdoc />
    public virtual Task<WfEngineResult> TransferAsync(
        long taskId,
        long userId,
        long toUserId,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        return engine.ExecuteAsync(
            new TransferTaskCmd
            {
                TaskId = taskId,
                UserId = userId,
                ToUserId = toUserId,
                Comment = comment,
            },
            cancellationToken);
    }

    /// <summary>同意/拒绝共用 CompleteTaskCmd。</summary>
    protected virtual Task<WfEngineResult> CompleteAsync(
        long taskId,
        long userId,
        WfTaskAction action,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (action is not (WfTaskAction.Approve or WfTaskAction.Reject))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["action"] = action.ToString() });
        }

        return engine.ExecuteAsync(
            new CompleteTaskCmd
            {
                TaskId = taskId,
                UserId = userId,
                Action = action,
                Comment = comment,
            },
            cancellationToken);
    }

    /// <summary>按定义收窄活跃待办 Id;未指定定义返回 null(不过滤)。</summary>
    protected virtual async Task<List<long>?> ResolveTaskIdsByDefinitionAsync(
        long? definitionId,
        CancellationToken cancellationToken)
    {
        var instanceIds = await ResolveInstanceIdsByDefinitionAsync(definitionId, cancellationToken);
        if (instanceIds is null)
            return null;
        if (instanceIds.Count == 0)
            return [];

        return await tasks.AsQueryable()
            .Where(t => instanceIds.Contains(t.InstanceId))
            .Select(t => t.Id)
            .ToListAsync();
    }

    /// <summary>按定义收窄实例 Id;未指定定义返回 null(不过滤)。</summary>
    protected virtual async Task<List<long>?> ResolveInstanceIdsByDefinitionAsync(
        long? definitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (definitionId is not > 0)
            return null;

        var versionIds = await versions.AsQueryable()
            .Where(v => v.DefinitionId == definitionId.Value && v.Version >= 1)
            .Select(v => v.Id)
            .ToListAsync();
        if (versionIds.Count == 0)
            return [];

        return await instances.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(i => versionIds.Contains(i.DefinitionVersionId))
            .Select(i => i.Id)
            .ToListAsync();
    }

    protected virtual async Task<PagedList<WfDoneItemOutput>> MapDonePageAsync(
        PagedList<WfHisTask> page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (page.Items.Count == 0)
        {
            return new PagedList<WfDoneItemOutput>
            {
                Current = page.Current,
                Size = page.Size,
                Total = page.Total,
                Items = [],
            };
        }

        var instanceIds = page.Items.Select(h => h.InstanceId).Distinct().ToList();
        var instanceMap = (await instances.AsQueryable()
                .ClearFilter<IOrgScoped>()
                .Where(i => instanceIds.Contains(i.Id))
                .ToListAsync())
            .ToDictionary(i => i.Id);
        var versionIds = instanceMap.Values.Select(i => i.DefinitionVersionId).Distinct().ToList();
        var versionMap = (await versions.AsQueryable().Where(v => versionIds.Contains(v.Id)).ToListAsync())
            .ToDictionary(v => v.Id);
        var defIds = versionMap.Values.Select(v => v.DefinitionId).Distinct().ToList();
        var defMap = (await definitions.AsQueryable()
                .ClearFilter<IOrgScoped>()
                .Where(d => defIds.Contains(d.Id))
                .ToListAsync())
            .ToDictionary(d => d.Id);

        var items = page.Items.Select(h =>
        {
            instanceMap.TryGetValue(h.InstanceId, out var instance);
            long defId = 0;
            var defName = "";
            var status = instance?.Status ?? WfInstanceStatus.Running;
            if (instance is not null && versionMap.TryGetValue(instance.DefinitionVersionId, out var ver))
            {
                defId = ver.DefinitionId;
                if (defMap.TryGetValue(ver.DefinitionId, out var def))
                    defName = def.Name;
            }

            return new WfDoneItemOutput
            {
                HisTaskId = h.Id,
                InstanceId = h.InstanceId,
                NodeId = h.NodeId,
                NodeName = h.NodeName,
                Action = h.Action,
                Comment = h.Comment,
                TransferToUserId = h.TransferToUserId,
                DefinitionId = defId,
                DefinitionName = defName,
                BusinessKey = instance?.BusinessKey,
                InstanceStatus = status,
                StarterUserId = instance?.StarterUserId ?? 0,
                CreateTime = h.CreateTime,
            };
        }).ToList();

        return new PagedList<WfDoneItemOutput>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = items,
        };
    }

    protected static string? ResolveNodeNameCached(
        long definitionVersionId,
        string nodeId,
        IReadOnlyDictionary<long, WfDefinitionVersion> versionMap,
        Dictionary<long, WfModel?> modelCache)
    {
        if (!modelCache.TryGetValue(definitionVersionId, out var model))
        {
            model = versionMap.TryGetValue(definitionVersionId, out var ver)
                ? WfModelJson.Deserialize(ver.ModelJson)
                : null;
            modelCache[definitionVersionId] = model;
        }

        if (model is null)
            return null;
        for (var n = model.Root; n is not null; n = n.Next)
        {
            if (n.Id == nodeId)
                return n.Name;
        }

        return null;
    }

    protected static PagedList<WfTodoItemOutput> EmptyTodoPage(WfTaskPageInput input) => new()
    {
        Current = input.Current <= 0 ? 1 : input.Current,
        Size = input.Size <= 0 ? 20 : input.Size,
        Total = 0,
        Items = [],
    };

    protected static PagedList<WfDoneItemOutput> EmptyDonePage(WfTaskPageInput input) => new()
    {
        Current = input.Current <= 0 ? 1 : input.Current,
        Size = input.Size <= 0 ? 20 : input.Size,
        Total = 0,
        Items = [],
    };
}
