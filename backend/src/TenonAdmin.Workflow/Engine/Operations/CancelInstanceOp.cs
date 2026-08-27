namespace TenonAdmin.Workflow;

/// <summary>撤销实例:改状态 + 清活跃任务 + 历史 + FormBinder 回调 + 排队通知。校验已在 WorkflowEngine.BeginCancelAsync 做完。</summary>
public class CancelInstanceOp : IWfOperation
{
    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 实例级 CAS:Cancel 不像 Approve/Reject 那样先抢任务级 Version 锁,这道闸门是唯一防线——防双击
        // 撤销并发两次都通过校验、也防撤销与"第一票批准"在快照隔离下彼此看不见对方而互相覆盖。
        // Task 9 把这里从「只锚状态」升级成「期望状态 + 版本」双条件(数据库评审 §4.1):光锚状态只能拦住
        // 第二次撤销,拦不住「撤销 vs 一次会推进 token 的同意」——那条路上实例状态在两边看都还是 Running。
        // 领取只推进版本、不碰业务列,所以下面的状态写照旧走整对象更新、照旧拿到审计 AOP 填充
        // (领取语句用的 SetColumns 走条件更新路径,不触发只认整对象更新的审计 AOP;见 ClaimInstanceAsync)。
        await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);
        ctx.Instance.Status = WfInstanceStatus.Cancelled;
        await ctx.Db.Updateable(ctx.Instance)
            .UpdateColumns(i => new { i.Status, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();

        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
        ctx.Token.Status = WfTokenStatus.Cancelled;
        await ctx.Db.Updateable(ctx.Token)
            .UpdateColumns(t => new { t.Status, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        var activeTask = await ctx.Db.Queryable<WfTask>()
            .Where(t => t.TokenId == ctx.Token.Id)
            .FirstAsync();
        if (activeTask is not null)
        {
            await ctx.Db.Updateable<WfTaskActor>()
                .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
                .Where(a => a.TaskId == activeTask.Id)
                .ExecuteCommandAsync();
            await ctx.Db.Deleteable<WfTaskActor>().Where(a => a.TaskId == activeTask.Id).ExecuteCommandAsync();
            await ctx.Db.Deleteable<WfTask>().In(activeTask.Id).ExecuteCommandAsync();
        }

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceCompleted,
            payload: new { status = WfInstanceStatus.Cancelled.ToString() },
            cancellationToken: cancellationToken);

        await ctx.FormBinder.OnInstanceCompletedAsync(
            new WfFormBindContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                VariablesJson = ctx.Instance.VariablesJson,
                Status = WfInstanceStatus.Cancelled,
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
            Status = WfInstanceStatus.Cancelled,
        };
    }
}
