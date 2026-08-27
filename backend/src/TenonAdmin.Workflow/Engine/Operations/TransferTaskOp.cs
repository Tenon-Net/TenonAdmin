namespace TenonAdmin.Workflow;

/// <summary>
/// 任务级转办(M1):当前办理人把**这一件**待办交给别人办,责任随之转移。
/// 动作序列全在 <see cref="ReassignTaskOpBase"/> 上(CAS 认领 Pending actor → 写 <c>wf_his_task</c> →
/// 挂上目标 Pending actor,不推进 token、不删待办),本类只声明「我是哪个动词」。
/// <para>与 <see cref="DelegateTaskOp"/>(委托)是**兄弟**、语义平级,不是父子:转办=把活儿交出去
/// (责任转移),委托=请人代办。两者走独立端点是有意的——本仓权限码即路由,合并端点等于让
/// 「可转办」与「可委托」永远只能一起授权。</para>
/// </summary>
public class TransferTaskOp(
    WfTask task,
    long userId,
    long toUserId,
    string? comment) : ReassignTaskOpBase(task, userId, toUserId, comment)
{
    /// <inheritdoc />
    protected override WfTaskAction HistoryAction => WfTaskAction.Transfer;

    /// <inheritdoc />
    protected override int TargetInvalidErrorCode => WorkflowErrorCode.TransferTargetInvalid;
}
