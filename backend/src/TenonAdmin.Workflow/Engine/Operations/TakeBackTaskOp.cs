using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 拿回自己最近通过的审批:关闭当前下游待办,保留历史,再以新的 NodeVisitId 重入原审批节点。
/// 并行 token 尚未进入本阶段,因此只接受同一主 token 上的最近审批。
/// </summary>
public class TakeBackTaskOp(
    WfTask task,
    long userId,
    string? comment) : IWfOperation
{
    protected WfTask Task { get; } = task;
    protected long UserId { get; } = userId;
    protected string? Comment { get; } = comment;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var approval = await FindRecentApprovalAsync(ctx, cancellationToken);
        if (approval is null)
            throw NotAllowed("noRecentApproval");

        if (approval.NodeVisitId is null || approval.NodeVisitId == Task.NodeVisitId)
            throw NotAllowed("approvalIsNotUpstream");

        var target = ctx.FindNode(approval.NodeId);
        if (target is null || target.Type != WfNodeType.Approval)
            throw NotAllowed("targetNotApproval");

        // 任务级 CAS 先于关闭 actor,与同一当前待办上的审批/改派/加减签互斥。
        var taskClaimed = await ctx.Db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = Task.Version + 1 })
            .Where(t => t.Id == Task.Id && t.Version == Task.Version)
            .ExecuteCommandAsync(cancellationToken);
        if (taskClaimed != 1)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });
        Task.Version++;

        // 任何更晚的人工历史都关闭拿回窗口。当前 task 只允许被创建,不属于人工动作。
        var downstreamAction = await ctx.Db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == ctx.Instance.Id
                        && h.TokenId == Task.TokenId
                        && h.Id > approval.Id)
            .AnyAsync(cancellationToken);
        if (downstreamAction)
            throw NotAllowed("downstreamActionExists");

        if (await HasTerminalExecutionAsync(ctx, approval, cancellationToken))
            throw NotAllowed("downstreamExecutionCompleted");

        var currentNode = ctx.FindNode(Task.NodeId)
                          ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                              new Dictionary<string, object?> { ["nodeId"] = Task.NodeId });
        var recalledActors = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.ActorType == WfActorType.Approver
                        && (a.Status == WfActorStatus.Pending || a.Status == WfActorStatus.Waiting))
            .ToListAsync(cancellationToken);
        var recalledUserIds = recalledActors.Select(actor => actor.UserId).ToList();
        var remainingActorIds = recalledActors.Select(actor => actor.Id).ToList();

        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);

        // 拿回改变 token 位置,先用 token 级 CAS,再用实例级 CAS 与撤销/终态写入隔离。
        await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);

        var newNodeVisitId = ctx.IdGenerator.NextId();
        await ctx.AppendHistoryAsync(
            WfHistoryEventType.NodeLeave,
            currentNode.Id,
            cancellationToken: cancellationToken);

        await CompleteTaskOp.CloseTaskAsync(ctx, Task, skipRemaining: true, cancellationToken, remainingActorIds);

        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = Task.InstanceId,
            NodeId = target.Id,
            NodeName = target.Name,
            TaskId = approval.TaskId,
            TokenId = Task.TokenId,
            UserId = UserId,
            OriginalUserId = approval.OriginalUserId,
            DelegationRuleId = approval.DelegationRuleId,
            DelegationScopeOrgId = approval.DelegationScopeOrgId,
            Action = WfTaskAction.TakeBack,
            Comment = Comment,
            DurationMs = 0,
            NodeVisitId = newNodeVisitId,
        }).ExecuteCommandAsync(cancellationToken);

        // AppendHistoryAsync 从 ctx.Token 取 NodeVisitId;先放入新访问号,随后 EnterNodeOp 用同一号码落库。
        ctx.Token.NodeVisitId = newNodeVisitId;
        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TakeBack,
            target.Id,
            new
            {
                taskId = Task.Id,
                sourceTaskId = approval.TaskId,
                targetNodeId = target.Id,
                nodeVisitId = newNodeVisitId,
                userId = UserId,
            },
            cancellationToken);

        if (recalledUserIds.Count > 0)
        {
            ctx.PendingTaskRecalledNotifications.Add((
                new WfNotifyContext
                {
                    InstanceId = ctx.Instance.Id,
                    DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                    BusinessKey = ctx.Instance.BusinessKey,
                    NodeId = currentNode.Id,
                    NodeName = currentNode.Name,
                    StarterUserId = ctx.Instance.StarterUserId,
                    Status = ctx.Instance.Status,
                },
                Task.Id,
                recalledUserIds.Distinct().ToList()));
        }

        ctx.Agenda.Plan(new EnterNodeOp(target, newNodeVisitId));
    }

    protected virtual async Task<WfHisTask?> FindRecentApprovalAsync(
        WfExecutionContext ctx,
        CancellationToken cancellationToken)
    {
        return await ctx.Db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == ctx.Instance.Id
                        && h.TokenId == Task.TokenId
                        && h.UserId == UserId
                        && h.Action == WfTaskAction.Approve)
            .OrderBy(h => h.Id, OrderByType.Desc)
            .FirstAsync(cancellationToken);
    }

    protected virtual async Task<bool> HasTerminalExecutionAsync(
        WfExecutionContext ctx,
        WfHisTask approval,
        CancellationToken cancellationToken)
    {
        var executions = await ctx.Db.Queryable<WfNodeExecution>()
            .Where(e => e.InstanceId == ctx.Instance.Id
                        && e.TokenId == Task.TokenId
                        && e.NodeVisitId != approval.NodeVisitId
                        && e.CreateTime >= approval.CreateTime)
            .Select(e => new { e.Id, e.Status })
            .ToListAsync(cancellationToken);
        if (executions.Any(e => e.Status is WfNodeExecutionStatus.Succeeded
            or WfNodeExecutionStatus.ManualFallback
            or WfNodeExecutionStatus.Cancelled
            or WfNodeExecutionStatus.Failed))
            return true;

        var executionIds = executions.Select(e => e.Id).ToList();
        if (executionIds.Count == 0)
            return false;

        var terminalOutbox = await ctx.Db.Queryable<WfOutbox>()
            .Where(o => executionIds.Contains(o.ExecutionId)
                        && (o.Status == WfOutboxStatus.Dispatched || o.Status == WfOutboxStatus.Failed))
            .AnyAsync(cancellationToken);
        return terminalOutbox;
    }

    private static AdminException NotAllowed(string reason) =>
        WorkflowErrorCode.Exception(
            WorkflowErrorCode.TakeBackNotAllowed,
            new Dictionary<string, object?> { ["reason"] = reason });
}
