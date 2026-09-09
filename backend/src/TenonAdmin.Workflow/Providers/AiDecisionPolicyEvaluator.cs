namespace TenonAdmin.Workflow;

/// <summary>
/// AI proposal 的服务端确定性 policy 参数。由 <see cref="WorkflowOptions.AiDecision"/> 绑定，
/// 并在工作流装配期验证。
/// </summary>
public sealed class AiDecisionPolicyOptions
{
    /// <summary>服务端 policy 版本，随审计结果记录。</summary>
    public string Version { get; set; } = "v0";

    /// <summary>可成为 shadow candidate 的最低置信度。</summary>
    public decimal MinimumConfidence { get; set; } = 0.80m;

    /// <summary>可成为 shadow candidate 的最少 evidence 数。</summary>
    public int MinimumEvidenceCount { get; set; } = 1;

    /// <summary>出现其中任意一个精确 risk flag 即归为高风险。</summary>
    public string[] HighRiskFlags { get; set; } = [];

    /// <summary>
    /// 服务端允许的规范 reason code。比较一律使用 Ordinal，且 code 必须符合
    /// <see cref="AiDecisionProposalParser.IsCanonicalReasonCode"/> 的大写规则。
    /// </summary>
    public string[] AllowedReasonCodes { get; set; } = ["POLICY_MATCH"];

    /// <summary>验证 policy 的可执行范围和精确字符串规则。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version) || Version.Length > 32 || Version.Any(char.IsControl))
        {
            throw new ArgumentException("Policy 版本无效。", nameof(Version));
        }

        if (MinimumConfidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumConfidence),
                MinimumConfidence,
                "MinimumConfidence 必须在 0 到 1 之间。");
        }

        if (MinimumEvidenceCount is < 1 or > AiDecisionProposalParser.MaximumEvidenceCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumEvidenceCount),
                MinimumEvidenceCount,
                $"MinimumEvidenceCount 必须在 1 到 {AiDecisionProposalParser.MaximumEvidenceCount} 之间。");
        }

        ValidateFlags(HighRiskFlags, nameof(HighRiskFlags), AiDecisionProposalParser.MaximumRiskFlagCharacters);
        ValidateReasonCodes(AllowedReasonCodes);
    }

    private static void ValidateFlags(IReadOnlyList<string>? flags, string parameterName, int maximumLength)
    {
        if (flags is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flag in flags)
        {
            if (string.IsNullOrWhiteSpace(flag) || flag.Length > maximumLength || !distinct.Add(flag))
            {
                throw new ArgumentException("风险标识必须为唯一的非空精确值。", parameterName);
            }
        }
    }

    private static void ValidateReasonCodes(IReadOnlyList<string>? reasonCodes)
    {
        if (reasonCodes is null)
        {
            throw new ArgumentNullException(nameof(AllowedReasonCodes));
        }

        if (reasonCodes.Count is < 1 or > AiDecisionProposalParser.MaximumReasonCodeCount)
        {
            throw new ArgumentException("AllowedReasonCodes 必须包含受限数量的规范 reason code。", nameof(AllowedReasonCodes));
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reasonCode in reasonCodes)
        {
            if (!AiDecisionProposalParser.IsCanonicalReasonCode(reasonCode) || !distinct.Add(reasonCode))
            {
                throw new ArgumentException(
                    "AllowedReasonCodes 必须是唯一的大写规范 reason code。",
                    nameof(AllowedReasonCodes));
            }
        }
    }
}

/// <summary>
/// 确定性的服务端 policy evaluator。模型 recommendation 始终保留在结果中，但不产生批准、拒绝、
/// 完成任务或推进 token 的命令。
/// </summary>
public class AiDecisionPolicyEvaluator : IAiDecisionPolicyEvaluator
{
    private readonly decimal _minimumConfidence;
    private readonly int _minimumEvidenceCount;
    private readonly HashSet<string> _highRiskFlags;
    private readonly HashSet<string> _allowedReasonCodes;
    private readonly string _version;

    public AiDecisionPolicyEvaluator(AiDecisionPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _minimumConfidence = options.MinimumConfidence;
        _minimumEvidenceCount = options.MinimumEvidenceCount;
        _highRiskFlags = new HashSet<string>(options.HighRiskFlags, StringComparer.Ordinal);
        _allowedReasonCodes = new HashSet<string>(options.AllowedReasonCodes, StringComparer.Ordinal);
        _version = options.Version;
    }

    /// <summary>
    /// 固定优先级：模型显式 manual / reject 先保留为可审计信号；其余 approve proposal 再按
    /// reason、evidence、风险、置信度依次 fail closed。优先级写死以保证同一输入永远得到同一分类。
    /// </summary>
    public virtual AiDecisionPolicyEvaluation Evaluate(AiDecisionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var classification = proposal.Recommendation switch
        {
            AiDecisionRecommendation.Manual => AiDecisionPolicyClassification.ManualRequested,
            AiDecisionRecommendation.Reject => AiDecisionPolicyClassification.RejectRecommended,
            _ when proposal.ReasonCodes.Any(reasonCode => !_allowedReasonCodes.Contains(reasonCode)) =>
                AiDecisionPolicyClassification.DisallowedReason,
            _ when proposal.Evidence.Count < _minimumEvidenceCount =>
                AiDecisionPolicyClassification.EvidenceInsufficient,
            _ when proposal.RiskFlags.Any(riskFlag => _highRiskFlags.Contains(riskFlag)) =>
                AiDecisionPolicyClassification.HighRisk,
            _ when proposal.Confidence < _minimumConfidence =>
                AiDecisionPolicyClassification.LowConfidence,
            _ => AiDecisionPolicyClassification.ShadowCandidate,
        };

        return AiDecisionPolicyEvaluation.Create(proposal, classification, _version);
    }
}
