using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// <see cref="IWorkflowNotifier"/> 默认实现:对接 <see cref="IRealtimePublisher"/>。
/// <para><b>本类不吞异常</b>(M2c Task 7)。「通知失败不得拖垮审批事务」这条定案由**调用方**兑现 ——
/// 四个调用点(<see cref="WorkflowEngine.DispatchPendingNotificationsAsync"/> 的两处、
/// <c>WfTaskService.UrgeAsync</c>、<see cref="WfTimeoutJob"/> 的提醒路径)各自包着 <c>catch (Exception)</c>,
/// 并在那里记一条结构化 Warning。</para>
/// <para>此前本类**自己也**吞一层,于是成了双层网:内置实现的失败被内层吃掉、永远到不了外层那个网,
/// 也就永远不会被记录 —— 而消费者替换掉本类之后,失败又只落在外层。两头都想覆盖就只能删掉内层这一层。
/// 行为没有变化(异常照样不会浮到 HTTP 或事务),变化的是它**不再无声**。</para>
/// </summary>
public class WfDefaultNotifier(IRealtimePublisher realtimePublisher) : IWorkflowNotifier
{
    /// <inheritdoc />
    public virtual async Task TaskAssignedAsync(
        WfNotifyContext ctx,
        IReadOnlyList<long> userIds,
        CancellationToken cancellationToken = default)
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

    /// <inheritdoc />
    public virtual async Task InstanceCompletedAsync(
        WfNotifyContext ctx,
        CancellationToken cancellationToken = default) =>
        await realtimePublisher.NotifyUserAsync(
            ctx.StarterUserId,
            "workflow-instance-completed",
            new { ctx.InstanceId, ctx.BusinessKey },
            cancellationToken);

    /// <inheritdoc />
    public virtual async Task TaskUrgedAsync(
        WfNotifyContext ctx,
        long taskId,
        long? fromUserId,
        IReadOnlyList<long> toUserIds,
        CancellationToken cancellationToken = default)
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
}
