using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批任务服务(M1:待办/已办列表 + 同意/拒绝/转办)。Controller 调本接口 → 引擎 Cmd;
/// 方法全 <c>virtual</c>,消费者可继承覆写单步或前置 <c>TryAdd</c> 整体替换。
/// </summary>
public interface IWfTaskService
{
    /// <summary>我的待办分页(<c>wf_task_actor</c> Pending Approver)。</summary>
    Task<PagedList<WfTodoItemOutput>> PageTodoAsync(
        long userId,
        WfTaskPageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>我的已办分页(<c>wf_his_task</c> 本用户办理记录)。</summary>
    Task<PagedList<WfDoneItemOutput>> PageDoneAsync(
        long userId,
        WfTaskPageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>同意待办并推进(或签一票 / 顺序下一位 / 会签计票)。</summary>
    Task<WfEngineResult> ApproveAsync(
        long taskId,
        long userId,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>拒绝待办;M1 一律终止实例(节点 onReject=toNode 属 M2)。</summary>
    Task<WfEngineResult> RejectAsync(
        long taskId,
        long userId,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>任务级转办:把待办交给 <paramref name="toUserId"/>,不推进 token。</summary>
    Task<WfEngineResult> TransferAsync(
        long taskId,
        long userId,
        long toUserId,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 催办:仅发起人可对当前待办的 Pending 办理人发起一次提醒。不推进 token、不改任何任务/实例状态,
    /// 只追加一条 <see cref="WfHistoryEventType.TaskUrged"/> 历史事件并派发通知;可重复调用,无限流。
    /// </summary>
    Task UrgeAsync(
        long taskId,
        long callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 主动退回:把待办退回给之前某个节点(按节点 <see cref="WfReturnPolicy"/> 解析目标),不像
    /// <see cref="TransferAsync"/> 那样继续等人——关闭当前待办、token 回退,等发起人重提。
    /// <paramref name="targetNodeId"/> 仅 <see cref="WfReturnPolicy.Any"/> 策略有意义,其余策略忽略。
    /// </summary>
    Task<WfEngineResult> ReturnAsync(
        long taskId,
        long userId,
        string? targetNodeId,
        string? comment = null,
        CancellationToken cancellationToken = default);
}
