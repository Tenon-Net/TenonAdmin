namespace TenonAdmin.Workflow;

/// <summary>引擎命令标记。每条命令由 <see cref="IWorkflowEngine.ExecuteAsync"/> 在单一事务内消化。</summary>
public interface IWfCommand
{
}

/// <summary>引擎执行结果(事务已提交)。服务层据此发通知 / 返回 DTO。</summary>
public sealed class WfEngineResult
{
    public required long InstanceId { get; init; }

    public required WfInstanceStatus InstanceStatus { get; init; }

    /// <summary>本次新建的待办 Id(进入审批节点时);无则 null。</summary>
    public long? CreatedTaskId { get; init; }

    /// <summary>新建待办的办理人(通知用)。</summary>
    public IReadOnlyList<long> NewAssigneeUserIds { get; init; } = [];

    /// <summary>本次新抄送接收人。</summary>
    public IReadOnlyList<long> NewCcUserIds { get; init; } = [];
}

/// <summary>发起实例:建 instance + token,Agenda 从 start 自动推进到首个需停顿的节点。</summary>
public sealed class StartInstanceCmd : IWfCommand
{
    public required long DefinitionVersionId { get; init; }

    public required long StarterUserId { get; init; }

    /// <summary>发起人主属机构;写入 ApproverResolveContext,可空。</summary>
    public long? StarterOrgId { get; init; }

    public string? BusinessKey { get; init; }

    public string? VariablesJson { get; init; }

    /// <summary>按节点 Id 提交发起人自选审批人(仅 selfSelect Provider)。</summary>
    public IReadOnlyDictionary<string, List<long>>? SelectedUserIdsByNode { get; init; }
}

/// <summary>
/// 完成待办并推进 token。仅 <see cref="WfTaskAction.Approve"/>(前进)与
/// <see cref="WfTaskAction.Reject"/>(默认终止);转办走 <see cref="TransferTaskCmd"/>。
/// </summary>
public sealed class CompleteTaskCmd : IWfCommand
{
    public required long TaskId { get; init; }

    public required long UserId { get; init; }

    public required WfTaskAction Action { get; init; }

    public string? Comment { get; init; }
}

/// <summary>
/// 任务级转办:当前办理人把待办交给他人。不推进 token、不删待办;
/// 写 <c>wf_his_task</c>(Action=Transfer)后替换 actor。
/// </summary>
public sealed class TransferTaskCmd : IWfCommand
{
    public required long TaskId { get; init; }

    /// <summary>当前办理人(须为 Pending Approver)。</summary>
    public required long UserId { get; init; }

    /// <summary>转办目标用户。</summary>
    public required long ToUserId { get; init; }

    public string? Comment { get; init; }
}

/// <summary>
/// 任务级委托(一次性):当前办理人把这一件待办指给别人代办。机制与 <see cref="TransferTaskCmd"/>
/// 同构,只在 <c>wf_his_task</c> 记 <see cref="WfTaskAction.Delegate"/>;长期委托规则属 M3,不在此列。
/// </summary>
public sealed class DelegateTaskCmd : IWfCommand
{
    public required long TaskId { get; init; }

    /// <summary>当前办理人(须为 Pending Approver;实例发起人无权委托他人的待办)。</summary>
    public required long UserId { get; init; }

    /// <summary>委托目标用户。</summary>
    public required long ToUserId { get; init; }

    public string? Comment { get; init; }
}

/// <summary>
/// 超时触发:<see cref="WfTimeoutJob"/> 扫到一件到期待办后派一条本命令,由引擎在**同一事务**里
/// 领取(<c>taskId + Version + DueTime &lt;= now</c> 条件更新,设计规划 §14.1)、写
/// <see cref="WfHistoryEventType.TimeoutFired"/>、再等价入队人工动作的 Op。
/// <para>之所以不让 Job 直接拼 <see cref="CompleteTaskCmd"/> / <see cref="TransferTaskCmd"/>:
/// ①<c>TimeoutFired</c> 必须与动作同事务落库,否则崩在中间就只剩一行「张三同意了」而没有任何超时
/// 痕迹,审计误导变永久;②§14.1 的领取 CAS 只有在事务内才消掉「领取与动作之间的窗口」;
/// ③会签需要一次事务里对多个 Pending 办理人各记一次,那两条命令的形状表达不了。</para>
/// <para><see cref="WfTimeoutAction.Remind"/> **不走本命令**——它什么状态都不改,由 Job 直接写事件
/// 并推送(见 <see cref="WfTimeoutJob"/> 上关于「提醒不做版本 CAS」的说明)。</para>
/// </summary>
public sealed class TimeoutFireCmd : IWfCommand
{
    public required long TaskId { get; init; }

    /// <summary>扫描时读到的 <see cref="WfTask.Version"/>;领取 CAS 对不上即人工动作已胜出。</summary>
    public required int ExpectedVersion { get; init; }

    /// <summary>节点配置的超时动作;<see cref="WfTimeoutAction.Remind"/> 非法(不进引擎)。</summary>
    public required WfTimeoutAction Action { get; init; }

    /// <summary><see cref="WfTimeoutAction.Transfer"/> 时的目标用户。</summary>
    public long? TransferUserId { get; init; }

    /// <summary>
    /// 落进 <c>wf_his_task.Comment</c> 的说明文案。超时动作以**当前 Pending 办理人**的身份记
    /// **原生动词**(见 <see cref="WorkflowEngine.BeginTimeoutAsync"/>),不读事件流的视图光看
    /// 「张三同意了」会误解,故一并写上系统触发说明。
    /// </summary>
    public string? Comment { get; init; }
}

/// <summary>撤销实例:仅发起人、仅无人已批的 Running 实例可撤销。</summary>
public sealed class CancelInstanceCmd : IWfCommand
{
    public required long InstanceId { get; init; }

    public required long CallerUserId { get; init; }
}

/// <summary>
/// 主动退回:当前办理人把待办退回给之前某个节点。目标节点按节点 <see cref="WfReturnPolicy"/> 解析
/// (<c>Node</c>/<c>Prev</c> 忽略 <see cref="TargetNodeId"/>;<c>Any</c> 才用它)。不像
/// <see cref="TransferTaskCmd"/> 那样继续在原节点等人——关闭当前待办、token 回退到目标节点、
/// Agenda 留空,等发起人重提(<see cref="ResubmitInstanceCmd"/>)。
/// </summary>
public sealed class ReturnTaskCmd : IWfCommand
{
    public required long TaskId { get; init; }

    /// <summary>当前办理人(须为 Pending Approver)。</summary>
    public required long UserId { get; init; }

    /// <summary><see cref="WfReturnPolicy.Any"/> 时的目标节点 Id;其余策略忽略。</summary>
    public string? TargetNodeId { get; init; }

    public string? Comment { get; init; }
}

/// <summary>
/// 发起人重提:仅 <see cref="ReturnTaskCmd"/> 退回后、尚无活跃待办的 Running 实例可重提。
/// 从 <c>start</c> 重新走一遍(连已批过的节点也重新审),复用同一实例行。
/// </summary>
public sealed class ResubmitInstanceCmd : IWfCommand
{
    public required long InstanceId { get; init; }

    public required long CallerUserId { get; init; }

    public string? VariablesJson { get; init; }

    public IReadOnlyDictionary<string, List<long>>? SelectedUserIdsByNode { get; init; }
}
