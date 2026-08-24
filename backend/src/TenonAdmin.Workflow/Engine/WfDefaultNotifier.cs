using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// <see cref="IWorkflowNotifier"/> 默认实现:对接 <see cref="IRealtimePublisher"/>。
/// 通知失败(如消费者接自有网关实现抛异常)不得拖垮审批事务——每个方法内部吞异常,
/// 不加日志依赖(没有现成 <c>ILogger</c> 注入点,静默失败可接受,对齐
/// <see cref="TenonAdmin.Workflow.WfConditionEvaluator"/> 里同类「宁可捕宽也不能炸事务」的取舍)。
/// </summary>
public class WfDefaultNotifier(IRealtimePublisher realtimePublisher) : IWorkflowNotifier
{
    /// <inheritdoc />
    public virtual async Task TaskAssignedAsync(
        WfNotifyContext ctx,
        IReadOnlyList<long> userIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var userId in userIds)
            {
                await realtimePublisher.NotifyUserAsync(
                    userId,
                    "workflow-task-assigned",
                    new { ctx.InstanceId, ctx.BusinessKey, ctx.NodeName },
                    cancellationToken);
            }
        }
        catch (Exception)
        {
            // 通知失败不得拖垮审批事务;静默吞掉。
        }
    }

    /// <inheritdoc />
    public virtual async Task InstanceCompletedAsync(
        WfNotifyContext ctx,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await realtimePublisher.NotifyUserAsync(
                ctx.StarterUserId,
                "workflow-instance-completed",
                new { ctx.InstanceId, ctx.BusinessKey },
                cancellationToken);
        }
        catch (Exception)
        {
            // 通知失败不得拖垮审批事务;静默吞掉。
        }
    }

    /// <inheritdoc />
    public virtual async Task TaskUrgedAsync(
        WfNotifyContext ctx,
        long taskId,
        long? fromUserId,
        IReadOnlyList<long> toUserIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var toUserId in toUserIds)
            {
                await realtimePublisher.NotifyUserAsync(
                    toUserId,
                    "workflow-task-urged",
                    new { ctx.InstanceId, taskId, fromUserId },
                    cancellationToken);
            }
        }
        catch (Exception)
        {
            // 通知失败不得拖垮审批事务;静默吞掉。
        }
    }
}
