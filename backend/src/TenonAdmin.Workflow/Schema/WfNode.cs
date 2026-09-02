using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenonAdmin.Workflow;

/// <summary>
/// 钉钉树节点(递归)。M1 为纯串行链:<c>start → approval|cc → … → null</c>;
/// <see cref="Conditions"/> 仅 <see cref="WfNodeType.Branch"/> 使用(M2)。
/// </summary>
public sealed class WfNode
{
    /// <summary>节点稳定 Id(设计器生成;退回/跳转按此引用)。</summary>
    public string Id { get; set; } = "";

    public WfNodeType Type { get; set; }

    public string Name { get; set; } = "";

    /// <summary>类型相关配置;缺省时引擎按出厂默认值处理。</summary>
    public WfNodeProps? Props { get; set; }

    /// <summary>条件分支臂(仅 <see cref="WfNodeType.Branch"/>);须恰好一条 <see cref="WfBranchArm.IsDefault"/>。</summary>
    public List<WfBranchArm>? Conditions { get; set; }

    /// <summary>串行后继;末节点为 <c>null</c>。branch 的汇合后继也走此字段。</summary>
    public WfNode? Next { get; set; }
}

/// <summary>
/// 节点 props 联合体——按 <see cref="WfNode.Type"/> 取用子集,未用字段保持 <c>null</c>/默认。
/// 配置纪律:抽屉默认可见 ≤5 项,其余进「高级」;全部有默认值。
/// </summary>
public sealed class WfNodeProps
{
    // ── start ──────────────────────────────────────────────

    /// <summary>可发起范围;空或 <c>null</c>=全员。</summary>
    public List<WfInitiatorScopeItem>? InitiatorScope { get; set; }

    // ── approval / cc ──────────────────────────────────────

    /// <summary>办理人解析:<c>provider</c> 键对应 <see cref="IApproverProvider.Key"/>。</summary>
    public WfAssignee? Assignee { get; set; }

    /// <summary>签核模式(仅 approval)。默认或签。</summary>
    public WfApprovalMode? Mode { get; set; }

    /// <summary>会签通过比例 1–100(M3 比例票签;M1/M2 忽略,视作 100)。</summary>
    public int? AllPassRatio { get; set; }

    /// <summary>拒绝走向。默认 <see cref="WfRejectAction.Terminate"/>。</summary>
    public WfRejectAction? OnReject { get; set; }

    /// <summary><see cref="WfRejectAction.ToNode"/> 时的目标节点 Id。</summary>
    public string? RejectToNodeId { get; set; }

    /// <summary>退回策略(M2)。</summary>
    public WfReturnPolicy? ReturnPolicy { get; set; }

    /// <summary><see cref="WfReturnPolicy.Node"/> 时的目标节点 Id。</summary>
    public string? ReturnToNodeId { get; set; }

    /// <summary>超时配置(M2);<c>null</c>=不启用。</summary>
    public WfTimeout? Timeout { get; set; }

    /// <summary>节点按钮文案(JNPF btnInfo);空/缺省=前端 i18n 默认。</summary>
    public WfButtonLabels? ButtonLabels { get; set; }

    /// <summary>空审批人策略(节点级;可覆盖流程默认与全局)。</summary>
    public WfNobodyAction? Nobody { get; set; }

    /// <summary><see cref="WfNobodyAction.Transfer"/> 时的指定用户 Id。</summary>
    public long? NobodyTransferUserId { get; set; }

    /// <summary>
    /// 字段权限矩阵(M1 预留空数组,M3 启用)。
    /// 仅 approval 有意义;cc 忽略。
    /// </summary>
    public List<WfFormFieldPerm>? FormPerms { get; set; }

    // ── webhook (M3a-1 Task 8) ───────────────────────────────

    /// <summary>Webhook URL(仅 <see cref="WfNodeType.Webhook"/>)。</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// HTTP 方法;<c>null</c> 默认 <c>POST</c>。白名单 <c>GET/POST/PUT/PATCH/DELETE/HEAD</c>
    /// (大小写不敏感),不在表内由 <see cref="WebhookNodeHandler"/> 判 <c>TerminalFailure</c>/48030。
    /// </summary>
    public string? WebhookMethod { get; set; }

    /// <summary>
    /// 自定义请求头;每对经 <c>JobHttpFence.ValidateHeader</c> 校验,另拒绝 <c>Host</c>/<c>Content-Length</c>
    /// 两个名字(见 <see cref="WebhookNodeHandler.BuildRequest"/> 类注释)。
    /// </summary>
    public Dictionary<string, string?>? WebhookHeaders { get; set; }

    /// <summary>单次调用超时(秒);<c>null</c> 默认 30,钳到 <c>[1, 120]</c>(不报错)。</summary>
    public int? WebhookTimeoutSeconds { get; set; }

    /// <summary>
    /// 失败兜底:<see cref="WfWebhookFailureAction.Fail"/>(默认)按分类表落 <c>Failed</c>/重试;
    /// <see cref="WfWebhookFailureAction.Manual"/> 时本 handler 的每个 <c>TerminalFailure</c> 一律改吐
    /// <c>ManualFallback</c>(错误码/摘要照旧)。
    /// </summary>
    public WfWebhookFailureAction? WebhookOnFailure { get; set; }
}

/// <summary>Webhook 失败兜底(节点 <c>props.webhookOnFailure</c>;M3a-1 Task 8)。JSON:<c>fail|manual</c>。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum WfWebhookFailureAction
{
    /// <summary>按分类表落 Failed/重试(出厂默认)</summary>
    Fail,

    /// <summary>该 handler 的每个 TerminalFailure 一律改吐 ManualFallback</summary>
    Manual,
}

/// <summary>发起范围一项(<c>props.initiatorScope[]</c>)。</summary>
public sealed class WfInitiatorScopeItem
{
    /// <summary>范围类型:<c>user</c> / <c>role</c> / <c>org</c>。</summary>
    public string Type { get; set; } = "";

    /// <summary>对应 Id。</summary>
    public long Id { get; set; }
}

/// <summary>办理人指定(<c>props.assignee</c>)。</summary>
public sealed class WfAssignee
{
    /// <summary><see cref="IApproverProvider.Key"/>(如 <c>leader</c> / <c>role</c> / <c>user</c>)。</summary>
    public string Provider { get; set; } = "";

    /// <summary>Provider 参数(如 <c>level</c> / <c>roleId</c>);形状由各 Provider 自定。</summary>
    public Dictionary<string, JsonElement>? Params { get; set; }
}

/// <summary>节点超时(<c>props.timeout</c>;M2)。</summary>
public sealed class WfTimeout
{
    public int Hours { get; set; }

    public WfTimeoutAction Action { get; set; } = WfTimeoutAction.Remind;

    /// <summary><see cref="WfTimeoutAction.Transfer"/> 时的目标用户 Id。</summary>
    public long? TransferUserId { get; set; }
}

/// <summary>节点动作按钮文案(<c>props.buttonLabels</c>);全可空,空=前端默认。</summary>
public sealed class WfButtonLabels
{
    public string? Approve { get; set; }

    public string? Reject { get; set; }

    public string? Return { get; set; }

    public string? Transfer { get; set; }

    public string? Delegate { get; set; }

    public string? Urge { get; set; }
}

/// <summary>条件分支的一条臂(<c>conditions[]</c>;M2)。</summary>
public sealed class WfBranchArm
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>结构化条件;默认臂可为 <c>null</c>。</summary>
    public WfConditionExpr? Expr { get; set; }

    /// <summary>是否默认臂(无匹配时走此臂;每 branch 恰好一条)。</summary>
    public bool IsDefault { get; set; }

    /// <summary>本臂子链;可 <c>null</c>(直接汇合到 branch.next)。</summary>
    public WfNode? Next { get; set; }
}

/// <summary>
/// 结构化条件表达式(非脚本)。叶子=<c>field+op+value</c>;组=<c>logic+children</c>。
/// 变量来源:发起时提交的摘要字段(见实例 <c>VariablesJson</c>)。
/// </summary>
public sealed class WfConditionExpr
{
    /// <summary>叶子:摘要字段名。</summary>
    public string? Field { get; set; }

    /// <summary>叶子:操作符。</summary>
    public WfConditionOp? Op { get; set; }

    /// <summary>叶子:比较值(类型随字段;数字/字符串/数组等)。</summary>
    public JsonElement? Value { get; set; }

    /// <summary>组:and/or。</summary>
    public WfConditionLogic? Logic { get; set; }

    /// <summary>组:子表达式。</summary>
    public List<WfConditionExpr>? Children { get; set; }
}
