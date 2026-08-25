namespace TenonAdmin.Workflow;

/// <summary>
/// 任务级委托(M2b,一次性):当前办理人把**这一件**待办指给别人代办,不是长期委托规则。
/// 机制与 <see cref="TransferTaskOp"/> 完全同构(CAS 认领 Pending actor → 写 <c>wf_his_task</c> →
/// 挂上目标 Pending actor,不推进 token、不删待办),故只覆写两个语义钩子。
/// <para>与转办的产品分工:转办=把活儿交出去(责任转移);委托=请人代办(委托人在
/// <c>wf_his_task</c> 里留一行 <see cref="WfTaskAction.Delegate"/>,与被委托人后续的办理动作各自成行)。
/// 两者走独立端点是有意的——本仓权限码即路由,合并端点等于让「可转办」与「可委托」无法分别授权。</para>
/// <para><b>不是向后跳转</b>:委托不改 token 所在节点,故 <see cref="WfTaskAction.Delegate"/> 不进
/// <c>EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync</c> 的跳转下界白名单——加进去会让委托误重置
/// 「同一人相邻节点去重」的基线。</para>
/// </summary>
public class DelegateTaskOp(
    WfTask task,
    long userId,
    long toUserId,
    string? comment) : TransferTaskOp(task, userId, toUserId, comment)
{
    /// <inheritdoc />
    protected override WfTaskAction HistoryAction => WfTaskAction.Delegate;

    /// <inheritdoc />
    protected override int TargetInvalidErrorCode => WorkflowErrorCode.DelegateTargetInvalid;
}
