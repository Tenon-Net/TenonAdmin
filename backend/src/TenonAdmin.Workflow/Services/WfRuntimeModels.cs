using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>发起流程入参(<c>POST .../instance/start</c>):定义 Id + 业务键 + 摘要变量。</summary>
public record WfStartInput
{
    /// <summary>流程定义 Id(须已发布)</summary>
    public long DefinitionId { get; init; }

    /// <summary>业务单据键(可空=纯审批)</summary>
    public string? BusinessKey { get; init; }

    /// <summary>发起摘要变量 JSON(金额/天数/类型…)</summary>
    public string? VariablesJson { get; init; }

    /// <summary>按节点 Id 提交发起人自选审批人(仅 selfSelect Provider 使用)</summary>
    public Dictionary<string, List<long>>? SelectedUserIdsByNode { get; init; }

    /// <summary>
    /// 幂等请求键(可空):同一次用户动作(含丢响应后的重试)携带同一个值,服务端据此返回<b>第一次</b>的结果,
    /// 而不是让重试撞在状态冲突上。去空白后须 ≤64 字符且不含换行,否则 48028;纯空白等同不传。
    /// </summary>
    public string? RequestId { get; init; }
}

/// <summary>当前用户可发起的已发布定义列表项。</summary>
public record WfStartableDefinitionOutput
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Icon { get; init; }
    public string? GroupName { get; init; }
    public int Version { get; init; }
    public string? FormComponent { get; init; }
}

/// <summary>当前用户可发起的已发布版本快照。</summary>
public record WfStartableDefinitionDetailOutput : WfStartableDefinitionOutput
{
    public long DefinitionVersionId { get; init; }
    public WfRuntimeModelOutput Model { get; init; } = new();
}

/// <summary>
/// 发起页与实例详情使用的公开模型投影。
/// <para>它刻意不复用 <see cref="WfModel"/>：执行端配置可能包含 Webhook 凭据、AI 指令、办理人参数
/// 和发起范围，不能跨运行时读取边界直接序列化。</para>
/// </summary>
public record WfRuntimeModelOutput
{
    public int Version { get; init; }
    public WfRuntimeNodeOutput Root { get; init; } = new();
    public WfFormSchema? FormSchema { get; init; }
    public string? FormComponent { get; init; }
}

/// <summary>运行时模型的节点拓扑投影。</summary>
public record WfRuntimeNodeOutput
{
    public string Id { get; init; } = "";
    public WfNodeType Type { get; init; }
    public string Name { get; init; } = "";
    public WfRuntimeNodePropsOutput? Props { get; init; }
    public IReadOnlyList<WfRuntimeBranchArmOutput>? Conditions { get; init; }
    public IReadOnlyList<WfRuntimeParallelArmOutput>? ParallelArms { get; init; }
    public WfRuntimeNodeOutput? Next { get; init; }
}

/// <summary>运行时节点允许客户端使用的公开配置。</summary>
public record WfRuntimeNodePropsOutput
{
    public WfRuntimeAssigneeOutput? Assignee { get; init; }
    public WfReturnPolicy? ReturnPolicy { get; init; }
    public WfButtonLabels? ButtonLabels { get; init; }
    public IReadOnlyList<WfFormFieldPerm>? FormPerms { get; init; }
}

/// <summary>仅公开办理人 provider，不公开 provider 参数。</summary>
public record WfRuntimeAssigneeOutput
{
    public string Provider { get; init; } = "";
}

public record WfRuntimeBranchArmOutput
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }
    public WfRuntimeNodeOutput? Next { get; init; }
}

public record WfRuntimeParallelArmOutput
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public WfRuntimeNodeOutput? Next { get; init; }
}

/// <summary>我发起的实例分页查询。</summary>
public record WfInstancePageInput : PageInputBase
{
    public WfInstanceStatus? Status { get; init; }

    public long? DefinitionId { get; init; }

    /// <summary>业务键精确匹配(可选)</summary>
    public string? BusinessKey { get; init; }
}

/// <summary>
/// 管理员监控分页:在 <see cref="WfInstancePageInput"/> 上叠加参与业务过滤。
/// userId 来自 query,不是「当前用户是发起人」。
/// </summary>
public record WfInstanceMonitorPageInput : WfInstancePageInput
{
    public long? StarterUserId { get; init; }

    /// <summary>办理人:当前 Pending actor 或历史 <c>wf_his_task</c>。</summary>
    public long? ActorUserId { get; init; }

    public long? CcUserId { get; init; }
}

/// <summary>待办 / 已办分页查询(当前用户)。</summary>
public record WfTaskPageInput : PageInputBase
{
    public long? DefinitionId { get; init; }
}

/// <summary>我的抄送分页查询(当前用户)。</summary>
public record WfCcPageInput : PageInputBase
{
    public long? DefinitionId { get; init; }

    /// <summary>只看未读;缺省=全部。</summary>
    public bool? OnlyUnread { get; init; }
}

/// <summary>抄送标已读入参。</summary>
public record WfCcMarkReadInput
{
    public long Id { get; init; }
}

/// <summary>抄送列表项(<c>wf_cc</c>,抄送≠待办)。</summary>
public record WfCcItemOutput
{
    public long Id { get; init; }
    public long InstanceId { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public string? BusinessKey { get; init; }
    public WfInstanceStatus InstanceStatus { get; init; }
    public long StarterUserId { get; init; }
    public bool IsRead { get; init; }
    public DateTime? ReadTime { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>审批动词入参(同意 / 拒绝 / 转办 / 委托 / 退回 / 催办)。</summary>
public record WfTaskActionInput
{
    public long TaskId { get; init; }

    public string? Comment { get; init; }

    /// <summary>内置表单办理值 JSON;仅同意/拒绝解释,其余动作忽略。</summary>
    public string? VariablesJson { get; init; }

    /// <summary>转办 / 委托目标用户 Id;仅 transfer 与 delegate 必填</summary>
    public long ToUserId { get; init; }

    /// <summary>退回目标节点 Id;仅节点 <see cref="WfReturnPolicy.Any"/> 策略时前端会传,其余动作忽略。</summary>
    public string? TargetNodeId { get; init; }

    /// <summary>
    /// 幂等请求键(可空):同一次用户动作(含丢响应后的重试)携带同一个值,服务端据此返回<b>第一次</b>的结果,
    /// 而不是让重试撞在状态冲突上。去空白后须 ≤64 字符且不含换行,否则 48028;纯空白等同不传。
    /// </summary>
    public string? RequestId { get; init; }

    // 催办(urge)与本 DTO 共用:字段收得到,但催办不进引擎(UrgeAsync 只追加事件 + 推通知),
    // 故控制器**不给催办透传**——催办可重复,不做幂等(见台账 ## 语义契约)。
}

/// <summary>撤销流程入参。</summary>
public record WfInstanceCancelInput
{
    public long InstanceId { get; init; }

    /// <summary>
    /// 幂等请求键(可空):同一次用户动作(含丢响应后的重试)携带同一个值,服务端据此返回<b>第一次</b>的结果,
    /// 而不是让重试撞在状态冲突上。去空白后须 ≤64 字符且不含换行,否则 48028;纯空白等同不传。
    /// </summary>
    public string? RequestId { get; init; }
}

/// <summary>重提流程入参(退回后发起人重新提交)。</summary>
public record WfInstanceResubmitInput
{
    public long InstanceId { get; init; }

    public string? VariablesJson { get; init; }

    public Dictionary<string, List<long>>? SelectedUserIdsByNode { get; init; }

    /// <summary>
    /// 幂等请求键(可空):同一次用户动作(含丢响应后的重试)携带同一个值,服务端据此返回<b>第一次</b>的结果,
    /// 而不是让重试撞在状态冲突上。去空白后须 ≤64 字符且不含换行,否则 48028;纯空白等同不传。
    /// </summary>
    public string? RequestId { get; init; }
}

/// <summary>我发起的 / 监控列表项。</summary>
public record WfInstanceListItemOutput
{
    public long Id { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public int Version { get; init; }
    public string? BusinessKey { get; init; }
    public WfInstanceStatus Status { get; init; }
    public string? VariablesJson { get; init; }
    public long StarterUserId { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>
/// 实例详情——进度 + 意见 + <see cref="FormComponent"/> 业务表单挂载点(前端动态挂载消费者组件)。
/// </summary>
public record WfInstanceDetailOutput
{
    public long Id { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public long DefinitionVersionId { get; init; }
    public int Version { get; init; }
    public string? BusinessKey { get; init; }
    public long StarterUserId { get; init; }
    public WfInstanceStatus Status { get; init; }
    public string? VariablesJson { get; init; }

    /// <summary>
    /// 开发者表单挂载点:消费者前端页面路径(来自模型 <c>formComponent</c>);
    /// 空=纯审批或走 M3 动态表单。
    /// </summary>
    public string? FormComponent { get; init; }

    public DateTime CreateTime { get; init; }

    /// <summary>当前用户在本实例上的待办(若有;详情页操作按钮用)</summary>
    public WfTodoItemOutput? MyPendingTask { get; init; }

    /// <summary>当前用户在本实例上的全部待办;并行臂可同时存在多条。</summary>
    public IReadOnlyList<WfTodoItemOutput> MyPendingTasks { get; init; } = [];

    /// <summary>审批意见时间线(<c>wf_his_task</c>,按时间升序)</summary>
    public IReadOnlyList<WfHisTaskOutput> HisTasks { get; init; } = [];

    /// <summary>实例绑定的已发布版本快照(不是定义当前草稿)。</summary>
    public WfRuntimeModelOutput? Model { get; init; }

    /// <summary>
    /// 流程图回放:最后一次向后跳转之后进入过的节点(含当前已进入未离开)。
    /// </summary>
    public IReadOnlyList<string> VisitedNodeIds { get; init; } = [];

    /// <summary>当前活跃待办所在节点(token 停顿处)。</summary>
    public IReadOnlyList<string> CurrentNodeIds { get; init; } = [];

    /// <summary>当前活跃任务 Id(发起人催办用;可空=无待办)。</summary>
    public long? CurrentTaskId { get; init; }

    /// <summary>当前实例的全部活跃任务(含并行臂上的 task/token 身份)。</summary>
    public IReadOnlyList<WfCurrentTaskOutput> CurrentTasks { get; init; } = [];

    /// <summary>按 fork 归组的并行运行态;旧串行实例为空。</summary>
    public IReadOnlyList<WfParallelForkOutput> ParallelForks { get; init; } = [];

    /// <summary>当前用户最近可拿回的主 token 待办;无资格时为空。</summary>
    public long? MyTakeBackTaskId { get; init; }
}

/// <summary>事件流条目(<c>wf_history</c>)。</summary>
public record WfHistoryItemOutput
{
    public long Id { get; init; }
    /// <summary>实例内严格递增序号;旧数据为 0。</summary>
    public int Sequence { get; init; }
    public WfHistoryEventType EventType { get; init; }
    public string? NodeId { get; init; }
    public long? TokenId { get; init; }
    public long? NodeVisitId { get; init; }
    /// <summary>历史内部载荷不属于运行时读取契约，始终为空。</summary>
    public string? PayloadJson { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>详情页当前活跃任务的最小目标身份。</summary>
public record WfCurrentTaskOutput
{
    public long TaskId { get; init; }
    public long TokenId { get; init; }
    public long? NodeVisitId { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
}

/// <summary>一次并行 fork 的回放投影。</summary>
public record WfParallelForkOutput
{
    public long ForkId { get; init; }
    public long ParentTokenId { get; init; }
    public long ParentNodeVisitId { get; init; }
    public string? NodeId { get; init; }
    public string? NodeName { get; init; }
    public WfTokenStatus ParentTokenStatus { get; init; }
    public int PendingArmCount { get; init; }
    public WfParallelForkStatus Status { get; init; }
    public IReadOnlyList<WfParallelArmOutput> Arms { get; init; } = [];
}

/// <summary>并行 fork 的回放状态(不是持久化状态)。</summary>
public enum WfParallelForkStatus
{
    Waiting = 1,
    Joined = 2,
    Cancelled = 3,
}

/// <summary>并行臂的运行态与当前子 token。</summary>
public record WfParallelArmOutput
{
    public long ForkId { get; init; }
    public string ArmId { get; init; } = "";
    public long ParentTokenId { get; init; }
    public long? ChildTokenId { get; init; }
    public WfParallelArmStatus Status { get; init; }
    public long ParentNodeVisitId { get; init; }
    public long? ChildEntryNodeVisitId { get; init; }
    public string? CurrentNodeId { get; init; }
    public string? CurrentNodeName { get; init; }
    public WfTokenStatus? ChildTokenStatus { get; init; }
    public string? Reason { get; init; }
}

/// <summary>AI Decision 审计的脱敏读取投影;不包含 proposal 原文、变量或执行内部标识。</summary>
public record WfAiDecisionAuditOutput
{
    public long Id { get; init; }
    public string NodeId { get; init; } = "";
    public int AttemptNo { get; init; }
    public AiDecisionProviderResultType ProviderResultType { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? InputHash { get; init; }
    public string? PromptVersion { get; init; }
    public string? ProposalSchemaVersion { get; init; }
    public bool? SchemaValid { get; init; }
    public string? PolicyVersion { get; init; }
    public AiDecisionPolicyClassification? PolicyClassification { get; init; }
    public AiDecisionRecommendation? Recommendation { get; init; }
    public decimal? Confidence { get; init; }
    public IReadOnlyList<string> RiskFlags { get; init; } = [];
    public IReadOnlyList<WfAiDecisionEvidenceRefOutput> EvidenceRefs { get; init; } = [];
    public long? LatencyMilliseconds { get; init; }
    public long? PromptTokens { get; init; }
    public long? CompletionTokens { get; init; }
    public long? TotalTokens { get; init; }
    public AiDecisionFallbackReason FallbackReason { get; init; }
    public bool ShadowMode { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>AI 审计中的受限证据引用;不包含证据正文。</summary>
public record WfAiDecisionEvidenceRefOutput
{
    public string Id { get; init; } = "";
    public string Source { get; init; } = "";
    public string ContentHash { get; init; } = "";
}

/// <summary>待办列表项。</summary>
public record WfTodoItemOutput
{
    public long TaskId { get; init; }
    public long ActorId { get; init; }
    public long InstanceId { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
    public WfSignMode SignMode { get; init; }
    public DateTime? DueTime { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public string? BusinessKey { get; init; }
    public long StarterUserId { get; init; }
    public string? VariablesJson { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>已办列表项(<c>wf_his_task</c>)。</summary>
public record WfDoneItemOutput
{
    public long HisTaskId { get; init; }
    public long InstanceId { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
    public WfTaskAction Action { get; init; }
    public string? Comment { get; init; }
    public long? OriginalUserId { get; init; }
    public long? DelegationRuleId { get; init; }
    public long? DelegationScopeOrgId { get; init; }
    public long? TransferToUserId { get; init; }
    /// <summary>加签 / 减签目标用户 Id。</summary>
    public long? TargetUserId { get; init; }

    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public string? BusinessKey { get; init; }
    public WfInstanceStatus InstanceStatus { get; init; }
    public long StarterUserId { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>
/// <see cref="IWorkflowNotifier"/> 各方法共用的通知上下文(建任务 / 实例完结 / 转办 / 催办)。
/// </summary>
public record WfNotifyContext
{
    public required long InstanceId { get; init; }
    public required long DefinitionVersionId { get; init; }
    public string? BusinessKey { get; init; }
    public string? NodeId { get; init; }
    public string? NodeName { get; init; }

    /// <summary>发起人用户 Id;<see cref="IWorkflowNotifier.InstanceCompletedAsync"/> 的通知对象。</summary>
    public required long StarterUserId { get; init; }

    /// <summary>
    /// 触发通知时刻的实例状态——待办到达时为 <see cref="WfInstanceStatus.Running"/>;
    /// 实例完结通知按实际终态传(<c>Approved</c>/<c>Rejected</c>),接收方据此区分通过还是拒绝。
    /// </summary>
    public required WfInstanceStatus Status { get; init; }
}

/// <summary>历史任务(详情时间线)。</summary>
public record WfHisTaskOutput
{
    public long Id { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
    public long UserId { get; init; }
    public long? OriginalUserId { get; init; }
    public long? DelegationRuleId { get; init; }
    public long? DelegationScopeOrgId { get; init; }
    public WfTaskAction Action { get; init; }
    public string? Comment { get; init; }
    public long? TransferToUserId { get; init; }
    /// <summary>加签 / 减签目标用户 Id。</summary>
    public long? TargetUserId { get; init; }
    public long DurationMs { get; init; }
    public DateTime CreateTime { get; init; }
}
