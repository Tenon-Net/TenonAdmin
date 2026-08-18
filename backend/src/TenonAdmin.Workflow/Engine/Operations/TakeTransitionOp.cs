namespace TenonAdmin.Workflow;

/// <summary>
/// token 离开当前节点 → 求后继(M1 纯串行取 <see cref="WfNode.Next"/>) → 进入下一节点或完结实例。
/// </summary>
public class TakeTransitionOp(WfNode fromNode) : IWfOperation
{
    protected WfNode FromNode { get; } = fromNode;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await ctx.AppendHistoryAsync(WfHistoryEventType.NodeLeave, FromNode.Id, cancellationToken: cancellationToken);

        var next = FromNode.Next;
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
        ctx.Instance.Status = status;
        await ctx.Db.Updateable(ctx.Instance)
            .UpdateColumns(i => new { i.Status, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();

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
    }
}
