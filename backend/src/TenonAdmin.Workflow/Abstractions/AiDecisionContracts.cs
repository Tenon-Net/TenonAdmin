using System.Collections.ObjectModel;
using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>
/// AI 决策 Provider 的事务外边界。Provider 只返回结构化 proposal 或可分类的受控状态，
/// 不参与工作流推进、人工任务或数据库写入。
/// </summary>
public interface IAiDecisionProvider
{
    Task<AiDecisionProviderResult> ProposeAsync(
        AiDecisionProviderRequest request,
        CancellationToken cancellationToken);
}

/// <summary>AI proposal 的受限 parser SPI。非法输入只能归为无效 proposal，不能把原始正文带入后续结果。</summary>
public interface IAiDecisionProposalParser
{
    AiDecisionProposalParseResult Parse(string? json);
}

/// <summary>AI proposal 的服务端确定性 policy SPI；只生成分类，不生成工作流动作命令。</summary>
public interface IAiDecisionPolicyEvaluator
{
    AiDecisionPolicyEvaluation Evaluate(AiDecisionProposal proposal);
}

/// <summary>
/// 传给 AI Provider 的最小执行身份和安全业务输入。刻意不含
/// <see cref="WfNodeExecutionContext.VariablesJson"/>；业务输入只能由 handler 按节点白名单构造。
/// </summary>
public sealed class AiDecisionProviderRequest
{
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyInputs =
        new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(StringComparer.Ordinal));

    public required string ExecutionKey { get; init; }

    public required long InstanceId { get; init; }

    public required long TokenId { get; init; }

    public long? NodeVisitId { get; init; }

    public required string NodeId { get; init; }

    public required long DefinitionVersionId { get; init; }

    /// <summary>数据范围锚点；显式带给 Provider，避免租户身份只靠隐式上下文推断。</summary>
    public long? OrgId { get; init; }

    public required long StarterUserId { get; init; }

    /// <summary>业务关联键；属于既有执行身份，不从 <c>VariablesJson</c> 推导。</summary>
    public string? BusinessKey { get; init; }

    /// <summary>1 基执行 attempt，和 execution attempt 写入序号保持同一口径。</summary>
    public required int Attempt { get; init; }

    /// <summary>本次调用的绝对 UTC 截止时刻。</summary>
    public required DateTimeOffset DeadlineAtUtc { get; init; }

    /// <summary>已校验的节点指令；只能由 Workflow 内置安全投影设置。</summary>
    public string Instructions { get; internal init; } = "";

    /// <summary>已选择、递归脱敏并冻结的业务输入；键按 Ordinal 排序。</summary>
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; internal init; } = EmptyInputs;

    /// <summary>安全输入的规范 JSON；不含原始变量或未选中字段。</summary>
    public string CanonicalInputsJson { get; internal init; } = "{}";

    /// <summary>规范化指令与安全输入共同计算的 SHA-256，小写十六进制并带算法前缀。</summary>
    public string InputHash { get; internal init; } =
        "sha256:10dd0c667075227a52aa56108ffb446c4fa609181626eb3905121387958889a4";
}

/// <summary>Provider 的封闭结果类型；没有「任意字符串成功/失败」这条旁路。</summary>
public enum AiDecisionProviderResultType
{
    Proposal = 1,
    TimedOut = 2,
    Failed = 3,
}

/// <summary>
/// Provider 的封闭结果。失败工厂接受的诊断文字立即丢弃，避免 adapter 的原始错误正文
/// 随类型化 hand-off、attempt 摘要或后续审计泄漏。
/// </summary>
public sealed class AiDecisionProviderResult
{
    private AiDecisionProviderResult(
        AiDecisionProviderResultType type,
        string? proposalJson,
        string? provider,
        string? model,
        string? promptVersion,
        long? promptTokens,
        long? completionTokens,
        long? totalTokens)
    {
        Type = type;
        ProposalJson = proposalJson;
        Provider = ValidateMetadata(provider, 64, nameof(provider));
        Model = ValidateMetadata(model, 128, nameof(model));
        PromptVersion = ValidateMetadata(promptVersion, 32, nameof(promptVersion));
        PromptTokens = ValidateTokens(promptTokens, nameof(promptTokens));
        CompletionTokens = ValidateTokens(completionTokens, nameof(completionTokens));
        TotalTokens = ValidateTokens(totalTokens, nameof(totalTokens));
    }

    public AiDecisionProviderResultType Type { get; }

    /// <summary>仅 <see cref="AiDecisionProviderResultType.Proposal"/> 时存在的未解析 proposal JSON。</summary>
    public string? ProposalJson { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public string? PromptVersion { get; }

    public long? PromptTokens { get; }

    public long? CompletionTokens { get; }

    public long? TotalTokens { get; }

    public static AiDecisionProviderResult Proposal(
        string proposalJson,
        string? provider = null,
        string? model = null,
        string? promptVersion = null,
        long? promptTokens = null,
        long? completionTokens = null,
        long? totalTokens = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalJson);
        return new AiDecisionProviderResult(
            AiDecisionProviderResultType.Proposal,
            proposalJson,
            provider,
            model,
            promptVersion,
            promptTokens,
            completionTokens,
            totalTokens);
    }

    public static AiDecisionProviderResult TimedOut(
        string? provider = null,
        string? model = null,
        string? promptVersion = null) =>
        new(
            AiDecisionProviderResultType.TimedOut,
            proposalJson: null,
            provider,
            model,
            promptVersion,
            promptTokens: null,
            completionTokens: null,
            totalTokens: null);

    public static AiDecisionProviderResult Failed(
        string? _,
        string? provider = null,
        string? model = null,
        string? promptVersion = null) =>
        new(
            AiDecisionProviderResultType.Failed,
            proposalJson: null,
            provider,
            model,
            promptVersion,
            promptTokens: null,
            completionTokens: null,
            totalTokens: null);

    private static string? ValidateMetadata(string? value, int maximumLength, string parameterName)
    {
        if (value is null) return null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException("Provider 审计元数据无效。", parameterName);
        return value;
    }

    private static long? ValidateTokens(long? value, string parameterName)
    {
        if (value is < 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

/// <summary>模型 proposal 的建议值；它是审计信息，不是工作流推进命令。</summary>
public enum AiDecisionRecommendation
{
    Approve = 1,
    Reject = 2,
    Manual = 3,
}

/// <summary>受限 evidence 引用；不保存 evidence 正文。</summary>
public sealed class AiDecisionEvidence
{
    private AiDecisionEvidence(string id, string source, string contentHash)
    {
        Id = id;
        Source = source;
        ContentHash = contentHash;
    }

    public string Id { get; }

    public string Source { get; }

    public string ContentHash { get; }

    /// <summary>构造受限 evidence 引用，不接受正文、空标识或非规范 content hash。</summary>
    public static AiDecisionEvidence Create(string id, string source, string contentHash)
    {
        ValidateNonEmpty(id, nameof(id), AiDecisionProposalParser.MaximumEvidenceIdCharacters);
        ValidateNonEmpty(source, nameof(source), AiDecisionProposalParser.MaximumEvidenceSourceCharacters);
        if (!IsValidContentHash(contentHash))
        {
            throw new ArgumentException("contentHash 必须是规范 sha256 摘要。", nameof(contentHash));
        }

        return new AiDecisionEvidence(id, source, contentHash);
    }

    internal static bool IsValidContentHash(string? value)
    {
        if (value is null
            || value.Length != "sha256:".Length + AiDecisionProposalParser.Sha256DigestCharacters
            || !value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value.AsSpan("sha256:".Length))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateNonEmpty(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException("值必须为受限长度的非空字符串。", parameterName);
        }
    }
}

/// <summary>
/// 已通过严格 schema 校验的 AI proposal。集合在构造时复制为只读快照，调用方不能通过
/// 公共表面改写解析结果再影响 policy。
/// </summary>
public sealed class AiDecisionProposal
{
    private AiDecisionProposal(
        string schemaVersion,
        AiDecisionRecommendation recommendation,
        decimal confidence,
        IEnumerable<string> reasonCodes,
        string rationale,
        IEnumerable<AiDecisionEvidence> evidence,
        IEnumerable<string> riskFlags)
    {
        SchemaVersion = schemaVersion;
        Recommendation = recommendation;
        Confidence = confidence;
        ReasonCodes = Freeze(reasonCodes);
        Rationale = rationale;
        Evidence = Freeze(evidence);
        RiskFlags = Freeze(riskFlags);
    }

    public string SchemaVersion { get; }

    public AiDecisionRecommendation Recommendation { get; }

    public decimal Confidence { get; }

    public IReadOnlyList<string> ReasonCodes { get; }

    public string Rationale { get; }

    public IReadOnlyList<AiDecisionEvidence> Evidence { get; }

    public IReadOnlyList<string> RiskFlags { get; }

    /// <summary>构造已通过 v1 schema 边界校验的不可变 proposal，供自定义 parser 返回类型化结果。</summary>
    public static AiDecisionProposal Create(
        string schemaVersion,
        AiDecisionRecommendation recommendation,
        decimal confidence,
        IEnumerable<string> reasonCodes,
        string rationale,
        IEnumerable<AiDecisionEvidence> evidence,
        IEnumerable<string> riskFlags)
    {
        if (!string.Equals(schemaVersion, AiDecisionProposalParser.SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("schemaVersion 不受支持。", nameof(schemaVersion));
        }

        if (recommendation is not (AiDecisionRecommendation.Approve
            or AiDecisionRecommendation.Reject
            or AiDecisionRecommendation.Manual))
        {
            throw new ArgumentOutOfRangeException(nameof(recommendation));
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        var validatedReasonCodes = ValidateReasonCodes(reasonCodes);
        ValidateNonEmpty(rationale, nameof(rationale), AiDecisionProposalParser.MaximumRationaleCharacters);
        var validatedEvidence = ValidateEvidence(evidence);
        var validatedRiskFlags = ValidateRiskFlags(riskFlags);

        return new AiDecisionProposal(
            schemaVersion,
            recommendation,
            confidence,
            validatedReasonCodes,
            rationale,
            validatedEvidence,
            validatedRiskFlags);
    }

    private static string[] ValidateReasonCodes(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var reasonCodes = values.ToArray();
        if (reasonCodes.Length is < 1 or > AiDecisionProposalParser.MaximumReasonCodeCount)
        {
            throw new ArgumentException("reasonCodes 必须包含受限数量的规范码。", nameof(values));
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reasonCode in reasonCodes)
        {
            if (!AiDecisionProposalParser.IsCanonicalReasonCode(reasonCode) || !distinct.Add(reasonCode))
            {
                throw new ArgumentException("reasonCodes 必须是唯一的规范 reason code。", nameof(values));
            }
        }

        return reasonCodes;
    }

    private static AiDecisionEvidence[] ValidateEvidence(IEnumerable<AiDecisionEvidence> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var evidence = values.ToArray();
        if (evidence.Length > AiDecisionProposalParser.MaximumEvidenceCount)
        {
            throw new ArgumentException("evidence 包含无效条目或超过上限。", nameof(values));
        }

        var distinct = new HashSet<(string Id, string Source, string ContentHash)>();
        foreach (var item in evidence)
        {
            if (item is null || !distinct.Add((item.Id, item.Source, item.ContentHash)))
            {
                throw new ArgumentException("evidence 必须是唯一的有效引用。", nameof(values));
            }
        }

        return evidence;
    }

    private static string[] ValidateRiskFlags(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var riskFlags = values.ToArray();
        if (riskFlags.Length > AiDecisionProposalParser.MaximumRiskFlagCount)
        {
            throw new ArgumentException("riskFlags 超过上限。", nameof(values));
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var riskFlag in riskFlags)
        {
            ValidateNonEmpty(riskFlag, nameof(values), AiDecisionProposalParser.MaximumRiskFlagCharacters);
            if (!distinct.Add(riskFlag))
            {
                throw new ArgumentException("riskFlags 必须是唯一的精确值。", nameof(values));
            }
        }

        return riskFlags;
    }

    private static void ValidateNonEmpty(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException("值必须为受限长度的非空字符串。", parameterName);
        }
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());
}

/// <summary>严格 proposal 解析的无异常结果；非法输入不携带原文或错误正文。</summary>
public sealed class AiDecisionProposalParseResult
{
    private AiDecisionProposalParseResult(AiDecisionProposal? proposal)
    {
        Proposal = proposal;
    }

    public bool IsValid => Proposal is not null;

    public AiDecisionProposal? Proposal { get; }

    /// <summary>包装已验证的 proposal 作为 parser 成功结果。</summary>
    public static AiDecisionProposalParseResult Valid(AiDecisionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return new AiDecisionProposalParseResult(proposal);
    }

    /// <summary>返回不携带原文或诊断的无效 parser 结果。</summary>
    public static AiDecisionProposalParseResult Invalid() => new(proposal: null);
}

/// <summary>服务端 policy 的确定性分类；任何值都仍然走人工兜底。</summary>
public enum AiDecisionPolicyClassification
{
    ShadowCandidate = 1,
    RejectRecommended = 2,
    ManualRequested = 3,
    LowConfidence = 4,
    HighRisk = 5,
    EvidenceInsufficient = 6,
    DisallowedReason = 7,
}

/// <summary>shadow-only hand-off 的人工兜底原因。</summary>
public enum AiDecisionFallbackReason
{
    ShadowOnly = 1,
    RejectNotAllowed = 2,
    ManualRequested = 3,
    LowConfidence = 4,
    HighRisk = 5,
    EvidenceInsufficient = 6,
    DisallowedReason = 7,
    MalformedProposal = 8,
    ProviderTimeout = 9,
    ProviderFailure = 10,
    AssigneeEmpty = 11,
}

/// <summary>policy 保留模型建议，同时给出服务端决定的分类。</summary>
public sealed class AiDecisionPolicyEvaluation
{
    private AiDecisionPolicyEvaluation(
        AiDecisionRecommendation recommendation,
        AiDecisionPolicyClassification classification,
        string policyVersion)
    {
        Recommendation = recommendation;
        Classification = classification;
        PolicyVersion = policyVersion;
    }

    public AiDecisionRecommendation Recommendation { get; }

    public AiDecisionPolicyClassification Classification { get; }

    public string PolicyVersion { get; }

    /// <summary>构造与 proposal recommendation 一致的确定性 policy 分类。</summary>
    public static AiDecisionPolicyEvaluation Create(
        AiDecisionProposal proposal,
        AiDecisionPolicyClassification classification,
        string policyVersion = "v0")
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (string.IsNullOrWhiteSpace(policyVersion)
            || policyVersion.Length > 32
            || policyVersion.Any(char.IsControl))
        {
            throw new ArgumentException("Policy 版本无效。", nameof(policyVersion));
        }

        var isCompatible = proposal.Recommendation switch
        {
            AiDecisionRecommendation.Approve => classification is AiDecisionPolicyClassification.ShadowCandidate
                or AiDecisionPolicyClassification.LowConfidence
                or AiDecisionPolicyClassification.HighRisk
                or AiDecisionPolicyClassification.EvidenceInsufficient
                or AiDecisionPolicyClassification.DisallowedReason,
            AiDecisionRecommendation.Reject => classification == AiDecisionPolicyClassification.RejectRecommended,
            AiDecisionRecommendation.Manual => classification == AiDecisionPolicyClassification.ManualRequested,
            _ => false,
        };

        if (!isCompatible)
        {
            throw new ArgumentException("proposal recommendation 与 policy classification 不一致。", nameof(classification));
        }

        return new AiDecisionPolicyEvaluation(proposal.Recommendation, classification, policyVersion);
    }
}

/// <summary>
/// 附着在 <see cref="WfNodeExecutionResult"/> 上的 AI 类型化交接数据。没有 Provider 错误正文、
/// 原始响应或原始变量 JSON。
/// </summary>
public sealed class AiDecisionOutcome
{
    private AiDecisionOutcome(
        AiDecisionProposal? proposal,
        AiDecisionRecommendation? recommendation,
        AiDecisionPolicyClassification? policyClassification,
        AiDecisionFallbackReason fallbackReason,
        AiDecisionProviderResultType providerResultType,
        string? inputHash,
        string? provider,
        string? model,
        string? promptVersion,
        long? promptTokens,
        long? completionTokens,
        long? totalTokens,
        string policyVersion)
    {
        Proposal = proposal;
        Recommendation = recommendation;
        PolicyClassification = policyClassification;
        FallbackReason = fallbackReason;
        ProviderResultType = providerResultType;
        InputHash = inputHash;
        Provider = provider;
        Model = model;
        PromptVersion = promptVersion;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        TotalTokens = totalTokens;
        PolicyVersion = policyVersion;
    }

    /// <summary>仅 proposal 通过 schema 时保留其受限、结构化表示。</summary>
    public AiDecisionProposal? Proposal { get; }

    public AiDecisionRecommendation? Recommendation { get; }

    public AiDecisionPolicyClassification? PolicyClassification { get; }

    public AiDecisionFallbackReason FallbackReason { get; }

    /// <summary>Provider 的封闭结果类型；审计只依赖此类型，不保存 Provider 原始响应。</summary>
    public AiDecisionProviderResultType ProviderResultType { get; }

    /// <summary>创建请求时计算的安全输入摘要；只保存摘要，不保存原始变量。</summary>
    public string? InputHash { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public string? PromptVersion { get; }

    public long? PromptTokens { get; }

    public long? CompletionTokens { get; }

    public long? TotalTokens { get; }

    public string PolicyVersion { get; }

    internal static AiDecisionOutcome ManualFallback(
        AiDecisionProposal? proposal,
        AiDecisionPolicyEvaluation? evaluation,
        AiDecisionFallbackReason fallbackReason,
        AiDecisionProviderResultType providerResultType = AiDecisionProviderResultType.Failed,
        string? inputHash = null,
        AiDecisionProviderResult? providerResult = null) =>
        new(
            proposal,
            evaluation?.Recommendation,
            evaluation?.Classification,
            fallbackReason,
            providerResultType,
            inputHash,
            providerResult?.Provider,
            providerResult?.Model,
            providerResult?.PromptVersion,
            providerResult?.PromptTokens,
            providerResult?.CompletionTokens,
            providerResult?.TotalTokens,
            evaluation?.PolicyVersion ?? "v0");

    internal static AiDecisionOutcome ManualFallback(
        AiDecisionFallbackReason fallbackReason,
        AiDecisionProviderResultType providerResultType = AiDecisionProviderResultType.Failed,
        string? inputHash = null,
        AiDecisionProviderResult? providerResult = null,
        string policyVersion = "v0") =>
        new(proposal: null, recommendation: null, policyClassification: null, fallbackReason, providerResultType,
            inputHash, providerResult?.Provider, providerResult?.Model, providerResult?.PromptVersion,
            providerResult?.PromptTokens, providerResult?.CompletionTokens, providerResult?.TotalTokens,
            policyVersion);

    internal AiDecisionOutcome WithFallbackReason(AiDecisionFallbackReason fallbackReason) =>
        new(
            Proposal,
            Recommendation,
            PolicyClassification,
            fallbackReason,
            ProviderResultType,
            InputHash,
            Provider,
            Model,
            PromptVersion,
            PromptTokens,
            CompletionTokens,
            TotalTokens,
            PolicyVersion);
}
