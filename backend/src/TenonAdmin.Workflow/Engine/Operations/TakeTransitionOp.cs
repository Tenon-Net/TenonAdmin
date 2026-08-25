namespace TenonAdmin.Workflow;

/// <summary>
/// token 离开当前节点 → 求汇合目标(主链节点直接取 <see cref="WfNode.Next"/>;分支臂末节点经
/// <see cref="WfExecutionContext.ResolveMergeTarget"/> 向上找外层 branch 的 <c>Next</c>,嵌套分支一路上溯)
/// → 进入下一节点或完结实例。树遍历全部委托 ctx/<see cref="WfModelIndex"/>,本类不做。
/// </summary>
public class TakeTransitionOp(WfNode fromNode) : IWfOperation
{
    protected WfNode FromNode { get; } = fromNode;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await ctx.AppendHistoryAsync(WfHistoryEventType.NodeLeave, FromNode.Id, cancellationToken: cancellationToken);

        var next = ctx.ResolveMergeTarget(FromNode);
        if (next is null)
        {
            await CompleteInstanceAsync(ctx, WfInstanceStatus.Approved, cancellationToken);
            return;
        }

        ctx.Agenda.Plan(new EnterNodeOp(next));
    }

    /// <summary>实例完结:翻状态、收 token、历史、表单挂载点回写。</summary>
    protected virtual async Task CompleteInstanceAsync(
        WfExecutionContext ctx,
        WfInstanceStatus status,
        CancellationToken cancellationToken)
    {
        // 终态写入前先领取实例与 token(数据库评审 §4.1):这是「终态写入 vs 重提」与「多 token 对同一
        // 实例终态」两类竞争的主出口——并行网关下同实例的两件待办各自通过任务级 CAS 后都会走到这里,
        // 双条件 CAS 只有一个能拿到 1 行,输的那个整事务回滚,不会留下半推进的实例。
        await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);
        ctx.Instance.Status = status;
        await ctx.Db.Updateable(ctx.Instance)
            .UpdateColumns(i => new { i.Status, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();

        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
        ctx.Token.Status = WfTokenStatus.Completed;
        await ctx.Db.Updateable(ctx.Token)
            .UpdateColumns(t => new { t.Status, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceCompleted,
            payload: new { status = status.ToString() },
            cancellationToken: cancellationToken);

        await ctx.FormBinder.OnInstanceCompletedAsync(
            new WfFormBindContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                VariablesJson = ctx.Instance.VariablesJson,
                Status = status,
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
            Status = status,
        };
    }
}
