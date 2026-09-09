using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// AI Decision 审计(<c>wf_ai_decision</c>)。每个已返回并进入 tx2 的 attempt 最多追加一行；
/// 不保存原始变量、完整 prompt、密钥、原始 Provider 响应或异常正文。
/// 本表只允许追加和保留期清理，基类的更新、软删除字段不得用于覆写历史审计。
/// </summary>
[SugarTable("wf_ai_decision", TableDescription = "AI Decision 审计")]
[SugarIndex("uk_wf_ai_decision_attempt",
    nameof(ExecutionId), OrderByType.Asc,
    nameof(AttemptNo), OrderByType.Asc,
    IsUnique = true)]
[SugarIndex("idx_wf_ai_decision_instance_time",
    nameof(InstanceId), OrderByType.Asc,
    nameof(CreateTime), OrderByType.Asc)]
public class WfAiDecision : AuditEntity
{
    [SugarColumn(ColumnDescription = "所属执行记录 Id")]
    public long ExecutionId { get; set; }

    [SugarColumn(ColumnDescription = "attempt 序号(1 基)")]
    public int AttemptNo { get; set; }

    [SugarColumn(ColumnDescription = "流程实例 Id")]
    public long InstanceId { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "节点 Id")]
    public string NodeId { get; set; } = "";

    [SugarColumn(ColumnDescription = "Provider 结果类型")]
    public AiDecisionProviderResultType ProviderResultType { get; set; }

    [SugarColumn(Length = 71, IsNullable = true, ColumnDescription = "Provider 标识哈希")]
    public string? Provider { get; set; }

    [SugarColumn(Length = 71, IsNullable = true, ColumnDescription = "模型标识哈希")]
    public string? Model { get; set; }

    /// <summary><c>sha256:</c> 加 64 位小写十六进制；只覆盖规范化、脱敏后的安全输入。</summary>
    [SugarColumn(Length = 71, IsNullable = true, ColumnDescription = "安全输入哈希")]
    public string? InputHash { get; set; }

    [SugarColumn(Length = 71, IsNullable = true, ColumnDescription = "Prompt 版本哈希")]
    public string? PromptVersion { get; set; }

    /// <summary>持久化前已规范化、脱敏的受限 proposal；格式无效时为空，不保存原始响应。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "规范化 proposal JSON")]
    public string? ProposalJson { get; set; }

    [SugarColumn(Length = 32, IsNullable = true, ColumnDescription = "Proposal schema 版本")]
    public string? ProposalSchemaVersion { get; set; }

    /// <summary><c>null</c> 表示 Provider 未返回可供 schema 校验的 proposal。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "Schema 是否有效")]
    public bool? SchemaValid { get; set; }

    [SugarColumn(Length = 71, IsNullable = true, ColumnDescription = "Policy 版本哈希")]
    public string? PolicyVersion { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "Policy 分类")]
    public AiDecisionPolicyClassification? PolicyClassification { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "模型建议")]
    public AiDecisionRecommendation? Recommendation { get; set; }

    [SugarColumn(Length = 18, DecimalDigits = 6, IsNullable = true, ColumnDescription = "模型置信度")]
    public decimal? Confidence { get; set; }

    /// <summary>只保存受限 risk flag 数组，不保存诊断正文。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "风险标记 JSON")]
    public string? RiskFlagsJson { get; set; }

    /// <summary>只保存 id/source/contentHash，不保存 evidence 正文。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "证据引用 JSON")]
    public string? EvidenceRefsJson { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "Provider 延迟(毫秒)")]
    public long? LatencyMilliseconds { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "输入 token 数")]
    public long? PromptTokens { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "输出 token 数")]
    public long? CompletionTokens { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "总 token 数")]
    public long? TotalTokens { get; set; }

    [SugarColumn(ColumnDescription = "人工兜底原因")]
    public AiDecisionFallbackReason FallbackReason { get; set; }

    /// <summary>V0 写入恒为 true；tx2 第二层强制规则将非兜底结果归一化为人工兜底。</summary>
    [SugarColumn(ColumnDescription = "是否 shadow-only")]
    public bool ShadowMode { get; set; } = true;
}
