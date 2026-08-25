using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 完成待办计票(M1:或签一票通过 / 顺序下一位 / 会签全票预留)。
/// 通过则 <see cref="TakeTransitionOp"/>;拒绝则终止实例。
/// </summary>
public class CompleteTaskOp(
    WfTask task,
    long userId,
    WfTaskAction action,
    string? comment) : IWfOperation
{
    protected WfTask Task { get; } = task;
    protected long UserId { get; } = userId;
    protected WfTaskAction Action { get; } = action;
    protected string? Comment { get; } = comment;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Action is not (WfTaskAction.Approve or WfTaskAction.Reject))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["action"] = Action.ToString() });
        }

        // 任务级 CAS:同一任务的并发动作只有一个事务能继续推进。
        var taskClaimed = await ctx.Db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = Task.Version + 1 })
            .Where(t => t.Id == Task.Id && t.Version == Task.Version)
            .ExecuteCommandAsync();
        if (taskClaimed != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });
        }
        Task.Version++;

        // 仅当前 Pending 办理人可翻 Done;顺序审批的后级仍是 Waiting。
        var claimed = await ctx.Db.Updateable<WfTaskActor>()
            .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Done })
            .Where(a => a.TaskId == Task.Id && a.UserId == UserId && a.Status == WfActorStatus.Pending
                        && a.ActorType == WfActorType.Approver)
            .ExecuteCommandAsync();
        if (claimed != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });
        }

        var node = ctx.FindNode(Task.NodeId)
                   ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                       new Dictionary<string, object?> { ["nodeId"] = Task.NodeId });
        ctx.CurrentNode = node;

        var durationMs = Math.Max(
            0,
            (long)(ctx.TimeProvider.GetLocalNow().DateTime - Task.CreateTime).TotalMilliseconds);
        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = Task.InstanceId,
            NodeId = Task.NodeId,
            NodeName = node.Name,
            TaskId = Task.Id,
            TokenId = Task.TokenId,
            UserId = UserId,
            Action = Action,
            Comment = Comment,
            DurationMs = durationMs,
        }).ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TaskCompleted,
            Task.NodeId,
            new { taskId = Task.Id, userId = UserId, action = Action.ToString() },
            cancellationToken);

        if (Action == WfTaskAction.Reject)
        {
            await CloseTaskAsync(ctx, skipRemaining: true, cancellationToken);
            await RejectInstanceAsync(ctx, node, cancellationToken);
            return;
        }

        // Approve 计票
        var passed = await TryPassAsync(ctx, cancellationToken);
        if (!passed)
            return; // 顺序/会签未满票:待办仍在,Agenda 空 → 等人

        await CloseTaskAsync(ctx, skipRemaining: true, cancellationToken);
        ctx.Agenda.Plan(new TakeTransitionOp(node));
    }

    /// <summary>或签立刻通过;顺序晋级下一位;会签看是否还有 Pending。</summary>
    protected virtual async Task<bool> TryPassAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (Task.SignMode)
        {
            case WfSignMode.Any:
                return true;

            case WfSignMode.Sequential:
                var next = await ctx.Db.Queryable<WfTaskActor>()
                    .Where(a => a.TaskId == Task.Id
                                && a.Status == WfActorStatus.Waiting
                                && a.ActorType == WfActorType.Approver)
                    .OrderBy(a => a.Sort, OrderByType.Asc)
                    .FirstAsync();
                if (next is null)
                    return true;

                var promoted = await ctx.Db.Updateable<WfTaskActor>()
                    .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Pending })
                    .Where(a => a.Id == next.Id && a.Status == WfActorStatus.Waiting)
                    .ExecuteCommandAsync();
                if (promoted != 1)
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                        new Dictionary<string, object?> { ["taskId"] = Task.Id });
                }
                ctx.NewAssigneeUserIds.Add(next.UserId);
                ctx.PendingTaskAssignedNotifications.Add((
                    new WfNotifyContext
                    {
                        InstanceId = ctx.Instance.Id,
                        DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                        BusinessKey = ctx.Instance.BusinessKey,
                        NodeId = Task.NodeId,
                        NodeName = ctx.CurrentNode?.Name,
                        StarterUserId = ctx.Instance.StarterUserId,
                        Status = ctx.Instance.Status,
                    },
                    [next.UserId]));
                return false;

            case WfSignMode.All:
            default:
                var pending = await ctx.Db.Queryable<WfTaskActor>()
                    .Where(a => a.TaskId == Task.Id && a.Status == WfActorStatus.Pending
                                && a.ActorType == WfActorType.Approver)
                    .AnyAsync();
                return !pending;
        }
    }

    protected virtual async Task CloseTaskAsync(
        WfExecutionContext ctx,
        bool skipRemaining,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (skipRemaining)
        {
            await ctx.Db.Updateable<WfTaskActor>()
                .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
                .Where(a => a.TaskId == Task.Id && a.Status == WfActorStatus.Pending)
                .ExecuteCommandAsync();
        }

        // 活跃待办完成即删(转历史已写入 wf_his_task)。
        await ctx.Db.Deleteable<WfTaskActor>().Where(a => a.TaskId == Task.Id).ExecuteCommandAsync();
        await ctx.Db.Deleteable<WfTask>().In(Task.Id).ExecuteCommandAsync();
    }

    /// <summary>
    /// 拒绝:节点未配置 <see cref="WfRejectAction"/> 或配为 <see cref="WfRejectAction.Terminate"/> 时终止整单;
    /// 配为 <see cref="WfRejectAction.ToNode"/> 时不终止,回退到 <see cref="WfNodeProps.RejectToNodeId"/> 指向的
    /// 节点重新进入(M2)——实例仍在正常审批流程中,后续一切交给 <see cref="EnterNodeOp"/> 处理。
    /// </summary>
    protected virtual async Task RejectInstanceAsync(
        WfExecutionContext ctx,
        WfNode node,
        CancellationToken cancellationToken)
    {
        if (node.Props?.OnReject == WfRejectAction.ToNode)
        {
            var target = ctx.FindNode(node.Props!.RejectToNodeId!)
                         ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                             new Dictionary<string, object?> { ["reason"] = "rejectTargetInvalid" });

            await ctx.AppendHistoryAsync(
                WfHistoryEventType.RejectRouted,
                node.Id,
                new { fromNodeId = node.Id, targetNodeId = target.Id },
                cancellationToken);
            await ctx.AppendHistoryAsync(WfHistoryEventType.NodeLeave, node.Id, cancellationToken: cancellationToken);
            ctx.Agenda.Plan(new EnterNodeOp(target));
            return;
        }

        ctx.Instance.Status = WfInstanceStatus.Rejected;
        await ctx.Db.Updateable(ctx.Instance)
            .UpdateColumns(i => new { i.Status, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();

        ctx.Token.Status = WfTokenStatus.Cancelled;
        await ctx.Db.Updateable(ctx.Token)
            .UpdateColumns(t => new { t.Status, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceCompleted,
            payload: new { status = WfInstanceStatus.Rejected.ToString() },
            cancellationToken: cancellationToken);

        await ctx.FormBinder.OnInstanceCompletedAsync(
            new WfFormBindContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                VariablesJson = ctx.Instance.VariablesJson,
                Status = WfInstanceStatus.Rejected,
                StarterUserId = ctx.Instance.StarterUserId,
            },
            cancellationToken);

        // 通知排队,事务提交后由 WorkflowEngine 统一派发。
        ctx.PendingInstanceCompletedNotification = new WfNotifyContext
        {
            InstanceId = ctx.Instance.Id,
            DefinitionVersionId = ctx.Instance.DefinitionVersionId,
            BusinessKey = ctx.Instance.BusinessKey,
            StarterUserId = ctx.Instance.StarterUserId,
            Status = WfInstanceStatus.Rejected,
        };
    }
}
