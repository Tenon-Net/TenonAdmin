using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流业务错误码(48xxx)。卫星包自管常量,强转 <see cref="ErrorCode"/> 抛 <see cref="AdminException"/>;
/// 前端按 <c>error.code.&lt;数字&gt;</c> 兜底键翻译(后续 API 轮次再补语义 MsgKey 与语言包)。
/// </summary>
public static class WorkflowErrorCode
{
    /// <summary>定义版本不存在或 ModelJson 无效</summary>
    public const int DefinitionVersionNotFound = 48001;

    /// <summary>流程模型非法(根非 start / 缺节点等)</summary>
    public const int ModelInvalid = 48002;

    /// <summary>实例不存在或已完结</summary>
    public const int InstanceNotFound = 48003;

    /// <summary>实例状态不允许本操作</summary>
    public const int InstanceStatusConflict = 48004;

    /// <summary>活跃 token 不存在</summary>
    public const int TokenNotFound = 48005;

    /// <summary>待办不存在(可能已被他人办完)</summary>
    public const int TaskNotFound = 48006;

    /// <summary>待办并发冲突(CAS 失败 / 非办理人)</summary>
    public const int TaskConflict = 48007;

    /// <summary>M1 不支持的节点类型</summary>
    public const int NodeTypeUnsupported = 48008;

    /// <summary>空审批人且策略为 block</summary>
    public const int NobodyBlocked = 48009;

    /// <summary>转办目标非法(≤0 / 转给自己 / 目标已在办理人中)</summary>
    public const int TransferTargetInvalid = 48010;

    /// <summary>流程定义不存在</summary>
    public const int DefinitionNotFound = 48011;

    /// <summary>定义状态不允许本操作(如已停用后再停用)</summary>
    public const int DefinitionStatusConflict = 48012;

    /// <summary>流程名称非法(空)</summary>
    public const int DefinitionNameInvalid = 48013;

    /// <summary>发起人非法(未登录 / userId≤0)</summary>
    public const int StarterInvalid = 48014;

    /// <summary>当前用户不是实例参与者</summary>
    public const int InstanceAccessDenied = 48015;

    /// <summary>当前用户不在定义的可发起范围内</summary>
    public const int InitiatorNotAllowed = 48016;

    /// <summary>流程模型字段超过持久化长度限制</summary>
    public const int ModelFieldTooLong = 48017;

    /// <summary>流程模型引用了未注册的审批人 Provider</summary>
    public const int ProviderNotRegistered = 48018;

    /// <summary>工作流事务或命令执行失败</summary>
    public const int OperationFailed = 48019;

    /// <summary>定义下仍有在途实例,不能删除</summary>
    public const int DefinitionHasRunningInstances = 48020;

    /// <summary>非发起人尝试催办</summary>
    public const int UrgeNotAllowed = 48021;

    /// <summary>非发起人撤销 / 已有人批准后撤销</summary>
    public const int CancelNotAllowed = 48023;

    /// <summary>退回策略未配置 / 目标节点非法</summary>
    public const int ReturnNotAllowed = 48024;

    /// <summary>非发起人重提 / 无待重提状态</summary>
    public const int ResubmitNotAllowed = 48025;

    /// <summary>委托目标非法(≤0 / 委托给自己 / 目标已在办理人中)</summary>
    public const int DelegateTargetInvalid = 48026;

    /// <summary>抄送行不存在或不属于当前用户</summary>
    public const int CcNotFound = 48027;

    /// <summary>
    /// 写命令的 <c>requestId</c> 非法:去空白后超过 64 字符(与 <c>wf_operation_receipt.RequestKey</c> 列宽对齐),
    /// 或含换行符(<c>WfIdentityHash</c> 用换行符做分隔符,值里出现即歧义)。
    /// <para>不复用 48017 <see cref="ModelFieldTooLong"/>:那条的语义写死在「流程<b>模型</b>字段」,
    /// 借它排障会被带向错误的方向。48022 是历史空号,不填回。</para>
    /// </summary>
    public const int RequestIdInvalid = 48028;

    /// <summary>Webhook 远端返回非成功状态码(M3a-1 Task 8);args 可带 <c>status</c></summary>
    public const int WebhookRequestFailed = 48029;

    /// <summary>Webhook 配置缺陷(URL/method/header 非法或被围栏拒绝);socket 未开(M3a-1 Task 8)</summary>
    public const int WebhookConfigInvalid = 48030;

    /// <summary>Webhook 传输层故障(超时/DNS/连接/TLS 等,M3a-1 Task 8)</summary>
    public const int WebhookTransportFailed = 48031;

    /// <summary>节点 handler 未分类异常(受控重试,摘要不含异常正文,Task 8b)</summary>
    public const int NodeHandlerUnhandled = 48032;

    public static AdminException Exception(int code, IReadOnlyDictionary<string, object?>? args = null) =>
        new((ErrorCode)code, args);
}
