using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>加签与减签共用任务级 CAS 的签核办理人变更动作。</summary>
internal abstract class SignTaskOp(
    WfTask task,
    long userId,
    long targetUserId,
    string? comment) : IWfOperation
{
    protected WfTask Task { get; } = task;
    protected long UserId { get; } = userId;
    protected long TargetUserId { get; } = targetUserId;
    protected string? Comment { get; } = comment;

    public abstract Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken);

    protected static Task<WfNode> LoadNodeAsync(
        WfExecutionContext ctx,
        string nodeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var node = ctx.FindNode(nodeId)
            ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["nodeId"] = nodeId });
        return System.Threading.Tasks.Task.FromResult(node);
    }

    protected static async Task EnsureCallerAndTargetScopeAsync(
        WfExecutionContext ctx,
        long callerId,
        long targetId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = await ctx.Db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == callerId || u.Id == targetId)
            .ToListAsync();
        var caller = users.FirstOrDefault(u => u.Id == callerId);
        var target = users.FirstOrDefault(u => u.Id == targetId);
        if (target is null || !target.Enabled || caller is null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignTargetInvalid,
                new Dictionary<string, object?> { ["targetUserId"] = targetId, ["reason"] = "userUnavailableOrOutOfScope" });
        }

        // 超管可跨机构加减签(种子超管 OrgId 常为 null);普通用户须同机构。
        if (!caller.IsSuperAdmin && caller.OrgId != target.OrgId)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignTargetInvalid,
                new Dictionary<string, object?> { ["targetUserId"] = targetId, ["reason"] = "userUnavailableOrOutOfScope" });
        }
    }

    protected async Task<WfTaskActor> RequirePendingCallerAsync(
        WfExecutionContext ctx,
        CancellationToken cancellationToken)
    {
        var actor = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.UserId == UserId
                        && a.ActorType == WfActorType.Approver && a.Status == WfActorStatus.Pending)
            .FirstAsync(cancellationToken);
        return actor ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignNotAllowed,
            new Dictionary<string, object?> { ["taskId"] = Task.Id, ["reason"] = "callerNotPending" });
    }

    protected async Task ClaimTaskAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        var affected = await ctx.Db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = Task.Version + 1 })
            .Where(t => t.Id == Task.Id && t.Version == Task.Version)
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });
        Task.Version++;
    }

    protected static async Task AppendHistoryAsync(
        WfExecutionContext ctx,
        WfTask task,
        WfTaskAction action,
        long userId,
        long targetUserId,
        string? comment,
        WfNode node,
        CancellationToken cancellationToken)
    {
        var actor = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == task.Id && a.UserId == userId
                        && a.ActorType == WfActorType.Approver && a.Status == WfActorStatus.Pending)
            .FirstAsync(cancellationToken);
        var now = ctx.TimeProvider.GetLocalNow().DateTime;
        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = task.InstanceId,
            NodeId = task.NodeId,
            NodeName = node.Name,
            TaskId = task.Id,
            TokenId = task.TokenId,
            UserId = userId,
            OriginalUserId = actor?.OriginalUserId,
            DelegationRuleId = actor?.DelegationRuleId,
            DelegationScopeOrgId = actor?.DelegationScopeOrgId,
            Action = action,
            Comment = comment,
            DurationMs = Math.Max(0, (long)(now - (actor?.ActivatedTime ?? task.CreateTime)).TotalMilliseconds),
            StartedTime = actor?.ActivatedTime,
            TargetUserId = targetUserId,
            NodeVisitId = task.NodeVisitId,
        }).ExecuteCommandAsync(cancellationToken);
    }

    protected static Task AppendSignEventAsync(
        WfExecutionContext ctx,
        WfTask task,
        WfTaskAction action,
        long userId,
        long targetUserId,
        int oldDenominator,
        int newDenominator,
        int? oldThreshold,
        int? newThreshold,
        CancellationToken cancellationToken) =>
        ctx.AppendHistoryAsync(
            WfHistoryEventType.SignChanged,
            task.NodeId,
            new
            {
                taskId = task.Id,
                userId,
                targetUserId,
                action = action.ToString(),
                oldDenominator,
                newDenominator,
                oldThreshold,
                newThreshold,
                nodeVisitId = task.NodeVisitId,
            },
            cancellationToken);

    protected static int? Threshold(WfTask task, WfNode node, int denominator) =>
        task.SignMode == WfSignMode.All
            ? Math.Max(1, (int)(((long)denominator * (node.Props?.AllPassRatio ?? 100) + 99) / 100))
            : null;
}

/// <summary>加签:追加 Pending/Waiting actor,不移动 token。</summary>
internal sealed class AddSignTaskOp(WfTask task, long userId, long targetUserId, string? comment)
    : SignTaskOp(task, userId, targetUserId, comment)
{
    public override async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TargetUserId <= 0 || TargetUserId == UserId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignTargetInvalid,
                new Dictionary<string, object?> { ["targetUserId"] = TargetUserId });

        await EnsureCallerAndTargetScopeAsync(ctx, UserId, TargetUserId, cancellationToken);
        var caller = await RequirePendingCallerAsync(ctx, cancellationToken);
        var alreadyActor = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.UserId == TargetUserId && a.ActorType == WfActorType.Approver)
            .AnyAsync(cancellationToken);
        if (alreadyActor)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignTargetInvalid,
                new Dictionary<string, object?> { ["targetUserId"] = TargetUserId, ["reason"] = "alreadyActor" });

        var node = await LoadNodeAsync(ctx, Task.NodeId, cancellationToken);
        if (node.Type != WfNodeType.Approval)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "notApprovalTask" });

        var actors = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.ActorType == WfActorType.Approver)
            .ToListAsync(cancellationToken);
        var oldDenominator = actors.Count(actor => actor.Status != WfActorStatus.Skipped);
        var oldThreshold = Threshold(Task, node, oldDenominator);
        await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);
        await ClaimTaskAsync(ctx, cancellationToken);
        var now = ctx.TimeProvider.GetLocalNow().DateTime;
        var status = Task.SignMode == WfSignMode.Sequential ? WfActorStatus.Waiting : WfActorStatus.Pending;
        await ctx.Db.Insertable(new WfTaskActor
        {
            TaskId = Task.Id,
            UserId = TargetUserId,
            ActorType = WfActorType.Approver,
            Status = status,
            Sort = actors.Count == 0 ? 1 : actors.Max(a => a.Sort) + 1,
            ActivatedTime = status == WfActorStatus.Pending ? now : null,
        }).ExecuteCommandAsync(cancellationToken);

        await AppendHistoryAsync(ctx, Task, WfTaskAction.AddSign, UserId, TargetUserId, Comment, node, cancellationToken);
        await AppendSignEventAsync(
            ctx,
            Task,
            WfTaskAction.AddSign,
            UserId,
            TargetUserId,
            oldDenominator,
            oldDenominator + 1,
            oldThreshold,
            Threshold(Task, node, oldDenominator + 1),
            cancellationToken);

        if (status == WfActorStatus.Pending)
        {
            ctx.NewAssigneeUserIds.Add(TargetUserId);
            ctx.PendingTaskAssignedNotifications.Add((new WfNotifyContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                NodeId = Task.NodeId,
                NodeName = node.Name,
                StarterUserId = ctx.Instance.StarterUserId,
                Status = ctx.Instance.Status,
            }, [TargetUserId]));
        }
    }
}

/// <summary>减签:将目标 actor 标记 Skipped,必要时关闭任务并推进 token。</summary>
internal sealed class RemoveSignTaskOp(WfTask task, long userId, long targetUserId, string? comment)
    : SignTaskOp(task, userId, targetUserId, comment)
{
    public override async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TargetUserId <= 0 || TargetUserId == UserId)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignTargetInvalid,
                new Dictionary<string, object?> { ["targetUserId"] = TargetUserId });

        await EnsureCallerAndTargetScopeAsync(ctx, UserId, TargetUserId, cancellationToken);
        await RequirePendingCallerAsync(ctx, cancellationToken);
        var target = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.UserId == TargetUserId
                        && a.ActorType == WfActorType.Approver)
            .FirstAsync(cancellationToken);
        if (target is null || target.Status is WfActorStatus.Done or WfActorStatus.Skipped)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignTargetInvalid,
                new Dictionary<string, object?> { ["taskId"] = Task.Id, ["reason"] = "targetNotRemovable" });

        var activeCount = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.ActorType == WfActorType.Approver && a.Status != WfActorStatus.Skipped)
            .CountAsync(cancellationToken);
        if (activeCount <= 1)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignTargetInvalid,
                new Dictionary<string, object?> { ["targetUserId"] = TargetUserId, ["reason"] = "lastActor" });

        var node = await LoadNodeAsync(ctx, Task.NodeId, cancellationToken);
        if (node.Type != WfNodeType.Approval)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.SignNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "notApprovalTask" });

        var oldThreshold = Threshold(Task, node, activeCount);
        var newDenominator = activeCount - 1;
        var newThreshold = Threshold(Task, node, newDenominator);
        var remainingActorIds = await CompleteTaskOp.FindRemainingActorIdsAsync(ctx, Task.Id, cancellationToken);
        await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);
        await ClaimTaskAsync(ctx, cancellationToken);
        var removed = await ctx.Db.Updateable<WfTaskActor>()
            .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
            .Where(a => a.Id == target.Id
                        && (a.Status == WfActorStatus.Pending || a.Status == WfActorStatus.Waiting))
            .ExecuteCommandAsync(cancellationToken);
        if (removed != 1)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });

        await AppendHistoryAsync(ctx, Task, WfTaskAction.RemoveSign, UserId, TargetUserId, Comment, node, cancellationToken);
        await AppendSignEventAsync(
            ctx,
            Task,
            WfTaskAction.RemoveSign,
            UserId,
            TargetUserId,
            activeCount,
            newDenominator,
            oldThreshold,
            newThreshold,
            cancellationToken);

        var affectedUserIds = await ctx.Db.Queryable<WfTaskActor>()
            .Where(actor => actor.TaskId == Task.Id
                            && actor.ActorType == WfActorType.Approver
                            && (actor.Status == WfActorStatus.Pending || actor.Status == WfActorStatus.Waiting))
            .Select(actor => actor.UserId)
            .ToListAsync(cancellationToken);
        affectedUserIds.Add(TargetUserId);
        ctx.PendingTaskSignChangedNotifications.Add((
            new WfNotifyContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                NodeId = Task.NodeId,
                NodeName = node.Name,
                StarterUserId = ctx.Instance.StarterUserId,
                Status = ctx.Instance.Status,
            },
            Task.Id,
            WfTaskAction.RemoveSign,
            affectedUserIds.Distinct().ToList()));

        if (Task.SignMode == WfSignMode.All)
        {
            var (passed, _) = await CompleteTaskOp.EvaluateAllAsync(ctx, Task, node, cancellationToken);
            if (passed)
            {
                await CompleteTaskOp.CloseTaskAsync(ctx, Task, skipRemaining: true, cancellationToken, remainingActorIds);
                ctx.Agenda.Plan(new TakeTransitionOp(node));
            }
        }
    }
}
