using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批任务服务:待办 / 已办两个列表 + 同意 / 拒绝 / 转办 / 委托 / 催办 / 退回六个动词。
/// Controller 调本接口 → 引擎 Cmd;实现方法全 <c>virtual</c>,消费者可继承覆写单步或前置
/// <c>TryAdd</c> 整体替换。
/// </summary>
/// <remarks>
/// M2b 期间本接口**逐轮新增方法**,是有意的源码级破坏性变更:Task 2 加
/// <see cref="UrgeAsync"/>、Task 5 加 <see cref="ReturnAsync"/>、Task 6 加 <see cref="DelegateAsync"/>。
/// 前置 <c>TryAdd</c> 自行实现 <see cref="IWfTaskService"/> 的消费者每轮都要同步补上新方法,
/// 否则编译失败;继承 <see cref="WfTaskService"/> 的消费者不受影响(新方法有内置实现)。
/// 不为兼容给接口加默认实现——那会让消费者静默漏掉新动词的准入校验,比编译失败更难发现。
/// M2b 收口后接口形状即冻结,后续动词(M3 的加减签 / 长期委托等)另开接口。
/// </remarks>
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
    /// 任务级委托(一次性):当前办理人把这一件待办指给 <paramref name="toUserId"/> 代办,不推进 token。
    /// 机制与 <see cref="TransferAsync"/> 同构,区别是 <c>wf_his_task</c> 记
    /// <see cref="WfTaskAction.Delegate"/>——转办是把活儿交出去,委托是请人代办。
    /// 实例发起人无权委托他人的待办(认领不到 Pending actor → <c>TaskConflict</c>);
    /// 允许链式委托,不设次数上限。长期委托规则属 M3,不在本方法语义内。
    /// </summary>
    Task<WfEngineResult> DelegateAsync(
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
