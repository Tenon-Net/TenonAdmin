namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流通知 SPI(待办到达 / 完结等)。默认实现可对接内核通知;
/// 消费者前置注册即整体替换。
/// </summary>
public interface IWorkflowNotifier
{
    /// <summary>待办到达:一批用户新增了一条待处理任务(建任务 / 转办 / 委托到达)。</summary>
    Task TaskAssignedAsync(
        WfNotifyContext ctx,
        IReadOnlyList<long> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>实例完结(批准或拒绝终止):通知发起人。</summary>
    Task InstanceCompletedAsync(WfNotifyContext ctx, CancellationToken cancellationToken = default);

    /// <summary>
    /// 催办:对 <paramref name="toUserIds"/>(当前 Pending 办理人)推一次提醒。
    /// <paramref name="fromUserId"/> 为 <c>null</c> 表示系统触发(供超时提醒复用)。
    /// </summary>
    Task TaskUrgedAsync(
        WfNotifyContext ctx,
        long taskId,
        long? fromUserId,
        IReadOnlyList<long> toUserIds,
        CancellationToken cancellationToken = default);
}
