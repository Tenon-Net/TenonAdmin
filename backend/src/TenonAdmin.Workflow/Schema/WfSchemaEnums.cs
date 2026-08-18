using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenonAdmin.Workflow;

/// <summary>
/// camelCase 字符串枚举(与 <see cref="WfModelJson.Options"/> / 设计草案一致)。
/// 挂在 schema 枚举上,让 MVC API 往返也是 <c>start|approval|…</c> 而非数字或 PascalCase。
/// </summary>
public sealed class CamelCaseEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true);

/// <summary>
/// 流程节点类型(schema <c>type</c>)。M1 仅启用 <see cref="Start"/> / <see cref="Approval"/> / <see cref="Cc"/>;
/// <see cref="Branch"/> 属 M2;<see cref="Parallel"/> / <see cref="Webhook"/> 属 M3。包容网关永久不做。
/// JSON 值为 camelCase:<c>start|approval|cc|branch|parallel|webhook</c>。
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfNodeType
{
    Start,
    Approval,
    Cc,
    Branch,
    Parallel,
    Webhook,
}

/// <summary>审批签核模式(节点 <c>props.mode</c>)。M1 实际只用或签;会签/顺序会签 M2 启用。JSON:<c>any|all|seq</c>。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfApprovalMode
{
    Any = 1,
    All = 2,

    /// <summary>顺序会签;成员名 <c>Seq</c> → JSON <c>seq</c>(对齐设计草案,非 sequential)。</summary>
    Seq = 3,
}

/// <summary>拒绝走向(节点 <c>props.onReject</c>)。JSON:<c>terminate|toNode</c>。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfRejectAction
{
    /// <summary>终止整单(出厂默认)</summary>
    Terminate,

    /// <summary>跳到指定节点(需配 <c>rejectToNodeId</c>)</summary>
    ToNode,
}

/// <summary>退回策略(节点 <c>props.returnPolicy</c>;M2)。JSON:<c>prev|any|node</c>。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfReturnPolicy
{
    Prev,
    Any,
    Node,
}

/// <summary>超时动作(节点 <c>props.timeout.action</c>;M2)。JSON:<c>remind|autoPass|autoReject|transfer</c>。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfTimeoutAction
{
    Remind,
    AutoPass,
    AutoReject,
    Transfer,
}

/// <summary>
/// 空审批人策略(节点 <c>props.nobody</c> / 流程默认 / <see cref="WorkflowOptions.Nobody"/>)。
/// 三级可配:节点 &gt; 流程默认 &gt; 全局配置。JSON:<c>autoPass|transfer|block</c>。
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfNobodyAction
{
    AutoPass,
    Transfer,
    Block,
}

/// <summary>条件表达式逻辑组;<c>and|or</c>。叶子节点不设。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfConditionLogic
{
    And,
    Or,
}

/// <summary>条件操作符(结构化 JSON,非脚本)。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfConditionOp
{
    Eq,
    Ne,
    Gt,
    Gte,
    Lt,
    Lte,
    In,
    NotIn,
    Contains,
    Empty,
    NotEmpty,
}

/// <summary>简易动态表单控件类型(M3;<see cref="WfFormSchema"/> 预留)。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfFormFieldType
{
    Text,
    Textarea,
    Number,
    Money,
    Date,
    Datetime,
    Select,
    MultiSelect,
    User,
    Attachment,
}

/// <summary>节点字段权限(<c>formPerms[].access</c>;M3 启用,M1 存空数组)。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfFormPermAccess
{
    Hidden,
    Readonly,
    Editable,
}
