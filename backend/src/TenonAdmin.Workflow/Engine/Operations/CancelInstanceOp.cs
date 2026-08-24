namespace TenonAdmin.Workflow;

/// <summary>撤销实例:改状态 + 清活跃任务 + 历史 + FormBinder 回调 + 排队通知。校验已在 WorkflowEngine.BeginCancelAsync 做完。</summary>
public class CancelInstanceOp : IWfOperation
{
    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 实例级 CAS:Cancel 不像 Approve/Reject 那样先抢任务级 Version 锁,这里的条件更新是唯一防线
        // ——防双击撤销并发两次都通过校验、也防撤销与"第一票批准"在快照隔离下彼此看不见对方而互相覆盖。
        // SetColumns 走条件更新路径,不会触发 SqlSugarSetup 里那个只认整对象更新的审计 AOP(见
        // SqlSugarRepository.SoftDeleteCoreAsync 同类注释),UpdateTime/UpdateUserId 须显式写;
        // Cancel 的授权规则已保证 caller == StarterUserId(BeginCancelAsync 校验过),直接取用。
        var claimed = await ctx.Db.Updateable<WfInstance>()
            .SetColumns(i => new WfInstance
            {
                Status = WfInstanceStatus.Cancelled,
                UpdateTime = ctx.TimeProvider.GetLocalNow().DateTime,
                UpdateUserId = ctx.Instance.StarterUserId,
            })
            .Where(i => i.Id == ctx.Instance.Id && i.Status == WfInstanceStatus.Running)
            .ExecuteCommandAsync();
        if (claimed != 1)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);
        ctx.Instance.Status = WfInstanceStatus.Cancelled;

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
