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
}
