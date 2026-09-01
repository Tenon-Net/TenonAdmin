namespace TenonAdmin.Workflow;

/// <summary>流程定义状态(<c>wf_definition.Status</c>)。</summary>
public enum WfDefinitionStatus
{
    /// <summary>草稿(未发布或编辑中)</summary>
    Draft = 0,

    /// <summary>已发布(可发起)</summary>
    Published = 1,

    /// <summary>停用(不可新发起;在途实例不受影响)</summary>
    Disabled = 2,
}

/// <summary>流程实例状态(<c>wf_instance.Status</c>)。</summary>
public enum WfInstanceStatus
{
    /// <summary>运行中</summary>
    Running = 1,

    /// <summary>审批通过(完结)</summary>
    Approved = 2,

    /// <summary>拒绝(完结)</summary>
    Rejected = 3,

    /// <summary>发起人撤销(完结;M2)</summary>
    Cancelled = 4,

    /// <summary>管理员终止(完结)</summary>
    Terminated = 5,
}

/// <summary>运行 token 状态(<c>wf_token.Status</c>)。一期串行≈每实例 1 活跃 token;并行网关启用后多 token。</summary>
public enum WfTokenStatus
{
    /// <summary>活跃(停在某节点)</summary>
    Active = 1,

    /// <summary>已完成(走过该路径)</summary>
    Completed = 2,

    /// <summary>已取消(退回/终止等)</summary>
    Cancelled = 3,
}

/// <summary>签核模式(<c>wf_task.SignMode</c>;schema 节点 <c>props.mode</c>)。M1 实际只用或签;会签/顺序 M2 启用。</summary>
public enum WfSignMode
{
    /// <summary>或签:任一办理人同意即通过</summary>
    Any = 1,

    /// <summary>会签:全部(或按比例)同意才通过</summary>
    All = 2,

    /// <summary>顺序会签:按办理人顺序逐个</summary>
    Sequential = 3,
}

/// <summary>任务办理人类型(<c>wf_task_actor.ActorType</c>)。</summary>
public enum WfActorType
{
    /// <summary>审批人</summary>
    Approver = 1,

    /// <summary>抄送接收人(抄送不算待办,另见 <c>wf_cc</c>)</summary>
    Cc = 2,
}

/// <summary>任务办理人状态(<c>wf_task_actor.Status</c>)。</summary>
public enum WfActorStatus
{
    /// <summary>顺序审批中尚未轮到</summary>
    Waiting = 0,

    /// <summary>待处理</summary>
    Pending = 1,

    /// <summary>已处理</summary>
    Done = 2,

    /// <summary>被转办/跳过等不再需处理</summary>
    Skipped = 3,
}

/// <summary>
/// 历史任务动作(<c>wf_his_task.Action</c>)。M1 只用同意/拒绝/转办;其余枚举值预留避免 M2/M3 加列或改枚举语义。
/// </summary>
public enum WfTaskAction
{
    /// <summary>同意</summary>
    Approve = 1,

    /// <summary>拒绝</summary>
    Reject = 2,

    /// <summary>转办</summary>
    Transfer = 3,

    /// <summary>退回(M2)</summary>
    Return = 4,

    /// <summary>撤销(M2)</summary>
    Cancel = 5,

    /// <summary>委托(M2)</summary>
    Delegate = 6,

    /// <summary>催办(M2)</summary>
    Urge = 7,

    /// <summary>加签(M3)</summary>
    AddSign = 8,

    /// <summary>减签(M3)</summary>
    RemoveSign = 9,

    /// <summary>拿回(M3)</summary>
    TakeBack = 10,
}

/// <summary>append-only 事件流类型(<c>wf_history.EventType</c>)。</summary>
public enum WfHistoryEventType
{
    /// <summary>实例创建</summary>
    InstanceStarted = 1,

    /// <summary>实例完结(通过/拒绝/撤销/终止)</summary>
    InstanceCompleted = 2,

    /// <summary>token 进入节点</summary>
    NodeEnter = 3,

    /// <summary>token 离开节点</summary>
    NodeLeave = 4,

    /// <summary>网关选出分支(M2)</summary>
    GatewayTaken = 5,

    /// <summary>超时触发(M2)</summary>
    TimeoutFired = 6,

    /// <summary>任务创建</summary>
    TaskCreated = 7,

    /// <summary>任务完成(同意/拒绝/转办等)</summary>
    TaskCompleted = 8,

    /// <summary>抄送已发</summary>
    CcSent = 9,

    /// <summary>催办</summary>
    TaskUrged = 10,

    /// <summary>同一人相邻节点自动跳过</summary>
    DuplicateApproverSkipped = 11,

    /// <summary>发起人重提(退回后重新提交,复用同一实例行)</summary>
    InstanceResubmitted = 12,

    /// <summary>拒绝路由(<c>onReject=toNode</c>:不终止实例,token 向后跳到目标节点重新进入)</summary>
    RejectRouted = 13,

    /// <summary>主动退回(办理人把 token 向后跳到目标节点,关闭当前待办后等发起人重提)</summary>
    TaskReturned = 14,
}

/// <summary>
/// 幂等回执覆盖的写命令(<c>wf_operation_receipt.CommandType</c>;设计规划 §14.2 列举的 8 个写命令)。
/// <para><b>枚举名参与 <see cref="WfIdentityHash"/> 计算,是发包后不可逆契约的一部分</b>:只允许追加新值,
/// 不得重命名、不得重排已有值(评审 §九 #6)。数值本身不进 hash,改数值不会改 identity,但同样不该动。</para>
/// <para><see cref="WfTaskAction.Approve"/> 与 <see cref="WfTaskAction.Reject"/> 虽同走
/// <see cref="CompleteTaskCmd"/>,这里刻意分成两个值——同人同任务同 request key 先拒后批必须是两条
/// identity,否则第二次动作会命中第一次的回执被当成重试。</para>
/// <para>催办(Urge)与超时(<see cref="TimeoutFireCmd"/>)<b>不在此列</b>:催办按语义可重复触发,
/// 超时是服务端 Job 派发、不是客户端重试。</para>
/// </summary>
public enum WfCommandType
{
    /// <summary>发起实例(<see cref="StartInstanceCmd"/>)</summary>
    Start = 1,

    /// <summary>同意(<see cref="CompleteTaskCmd"/> + <see cref="WfTaskAction.Approve"/>)</summary>
    Approve = 2,

    /// <summary>拒绝(<see cref="CompleteTaskCmd"/> + <see cref="WfTaskAction.Reject"/>)</summary>
    Reject = 3,

    /// <summary>转办(<see cref="TransferTaskCmd"/>)</summary>
    Transfer = 4,

    /// <summary>委托(<see cref="DelegateTaskCmd"/>)</summary>
    Delegate = 5,

    /// <summary>主动退回(<see cref="ReturnTaskCmd"/>)</summary>
    Return = 6,

    /// <summary>撤销实例(<see cref="CancelInstanceCmd"/>)</summary>
    Cancel = 7,

    /// <summary>发起人重提(<see cref="ResubmitInstanceCmd"/>)</summary>
    Resubmit = 8,
}

/// <summary>
/// 幂等回执的目标类型(<c>wf_operation_receipt.TargetType</c>)。枚举名同样参与
/// <see cref="WfIdentityHash"/>,约束与 <see cref="WfCommandType"/> 一致。
/// </summary>
public enum WfTargetType
{
    /// <summary>目标是实例(撤销/重提)</summary>
    Instance = 1,

    /// <summary>目标是待办(同意/拒绝/转办/委托/退回)</summary>
    Task = 2,

    /// <summary>
    /// 目标是定义版本——**仅 <see cref="WfCommandType.Start"/> 用**。发起时实例尚不存在,
    /// 没有 InstanceId 可锚,`(定义版本 + 发起人 + request key)` 足以定死一次发起。
    /// </summary>
    DefinitionVersion = 3,
}

/// <summary>
/// <c>wf_history</c> 事件的行为者类型(<c>wf_history.ActorType</c>;M3a-1)。回答「谁」,不是「干了什么」——
/// 「干了什么」已经由 <see cref="WfHistoryEventType"/> 表达,所以这里**不设 <c>Reminder</c>**:催办是真实
/// 用户点的按钮,由 <c>EventType = TaskUrged</c> 表达,行为者仍是 <see cref="Human"/>。
/// <para><b>不复用 <see cref="WfActorType"/></b>:那个枚举已经被 <c>wf_task_actor.ActorType</c>(审批人 /
/// 抄送接收人)占用,语义完全不同的两件事共用一个类型名只会在读代码时制造「这是哪张表的 ActorType」的
/// 歧义。</para>
/// <para><b>只追加、不重排</b>(评审 §九 #6,与 <see cref="WfCommandType"/>/<see cref="WfTargetType"/> 同款
/// 约束)。<see cref="Unknown"/> = 0 是升级前旧行的读出值(列带 <c>DefaultValue="0"</c>);
/// <see cref="Worker"/>/<see cref="Ai"/> 是评审 §4.6 点名预留的值,Task 1 里零写入点,先占位。</para>
/// </summary>
public enum WfHistoryActorType
{
    /// <summary>未知(升级前旧行的默认值)</summary>
    Unknown = 0,

    /// <summary>真实用户触发</summary>
    Human = 1,

    /// <summary>引擎/系统自身触发(如超时 Job 之外的系统写入)</summary>
    System = 2,

    /// <summary>超时 Job 触发</summary>
    Timeout = 3,

    /// <summary>后台 Worker 触发(预留)</summary>
    Worker = 4,

    /// <summary>AI 代理触发(预留)</summary>
    Ai = 5,
}

/// <summary>
/// 节点可靠执行记录状态(<c>wf_node_execution.Status</c>;M3a-1 Task 3)。
/// <para><b>与 <c>WfNodeExecutionResultType</c>(Task 5/6 引入)是两个类型,不许合并、不许共用数值</b>——前者是本表的
/// 生命周期状态,后者(Task 5/6 引入)是 handler 一次执行的结果分类,语义层面不同,合并只会制造「这个值到底
/// 指哪张表」的歧义。</para>
/// <para><b>刻意无 0 值</b>:与 <see cref="WfHistoryActorType.Unknown"/> = 0 的差别在于——那张表升级前有旧行
/// 需要一个「未知」默认值兜底,本表是 M3a-1 新建的表,不存在升级前的旧行,没有「默认值该是什么」这个问题。</para>
/// <para>状态转换图(Task 6 据此实现调度器,本 Task 只实现 <c>(insert) → Pending</c> 与
/// <c>Pending/RetryScheduled/Running → Running</c> 三条领取边):</para>
/// <code>
/// (insert) ──────────────► Pending
/// Pending ───── claim ───► Running
/// RetryScheduled ─claim──► Running        (NextRetryAtUtc &lt;= now)
/// Running ─── 租约过期 ──► Running        (重新领取;Fence + 1)
/// Running ──────────────► Succeeded | RetryScheduled | ManualFallback | Failed | Cancelled
/// Pending | RetryScheduled ─────────────► Cancelled
/// Succeeded / ManualFallback / Failed / Cancelled = 终态,无出边
/// </code>
/// <para><c>Running → Running</c> 是合法自转移,<b>这正是 <see cref="WfNodeExecution.Fence"/> 存在的原因</b>:
/// 老 owner 可能还活着(GC 停顿/网络分区未必等于进程已死),它的回写必须靠 fence 被拒,而不是靠「状态已经不是
/// Running 了」——因为状态仍然是 Running。</para>
/// </summary>
public enum WfNodeExecutionStatus
{
    /// <summary>待领取</summary>
    Pending = 1,

    /// <summary>已被领取,租约持有中</summary>
    Running = 2,

    /// <summary>执行成功(终态)</summary>
    Succeeded = 3,

    /// <summary>失败但预算未耗尽,等待下次重试(可再被领取)</summary>
    RetryScheduled = 4,

    /// <summary>转人工兜底(终态)</summary>
    ManualFallback = 5,

    /// <summary>
    /// 实例被外部撤销/终止,execution 作废(终态)。与 <see cref="Failed"/> 的差别:本值是外因(有人/有事件
    /// 主动叫停),<see cref="Failed"/> 是内因(handler 自己判定/预算耗尽)。
    /// </summary>
    Cancelled = 6,

    /// <summary>
    /// 失败且永不再动(终态)。触发条件二选一:handler 返回 <c>TerminalFailure</c>,或重试预算耗尽。
    /// </summary>
    Failed = 7,
}
