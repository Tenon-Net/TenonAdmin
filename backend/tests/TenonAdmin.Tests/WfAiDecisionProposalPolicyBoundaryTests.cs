using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M3b-0 Task 07 的 proposal/schema 与服务端 policy 边界：集合成员必须唯一，
/// 重复 evidence 不能虚增服务端证据计数，所有已完成路径仍停在人工兜底。
/// </summary>
public class WfAiDecisionProposalPolicyBoundaryTests
{
    private const string EvidenceHash =
        "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [MemberData(nameof(ProposalsWithDuplicateSetMembers))]
    public void Strict_parser_rejects_duplicate_set_members(string proposalJson)
    {
        var parsed = new AiDecisionProposalParser().Parse(proposalJson);

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    public static IEnumerable<object[]> ProposalsWithDuplicateSetMembers()
    {
        yield return [ProposalJson(reasonCodes: ["POLICY_MATCH", "POLICY_MATCH"])];
        yield return [ProposalJson(riskFlags: ["financial", "financial"])];
        yield return [ProposalJson(evidence: [Evidence("case-1"), Evidence("case-1")])];
    }

    [Fact]
    public void Public_validated_factory_rejects_duplicate_set_members()
    {
        var evidence = AiDecisionEvidence.Create("case-1", "workflow-input", EvidenceHash);
        var duplicateEvidence = AiDecisionEvidence.Create("case-1", "workflow-input", EvidenceHash);

        Assert.Throws<ArgumentException>(() => Proposal(
            reasonCodes: ["POLICY_MATCH", "POLICY_MATCH"]));
        Assert.Throws<ArgumentException>(() => Proposal(
            riskFlags: ["financial", "financial"]));
        Assert.Throws<ArgumentException>(() => Proposal(
            evidence: [evidence, duplicateEvidence]));
    }

    [Fact]
    public async Task Duplicate_evidence_cannot_satisfy_policy_minimum_and_routes_to_malformed_manual_fallback()
    {
        var provider = new StaticAiDecisionProvider(AiDecisionProviderResult.Proposal(
            ProposalJson(evidence: [Evidence("case-1"), Evidence("case-1")])));
        var handler = new AiDecisionNodeHandler(
            provider,
            new AiDecisionProposalParser(),
            Policy(minimumConfidence: 0.80m, minimumEvidenceCount: 2));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        var outcome = Assert.IsType<AiDecisionOutcome>(result.AiDecision);
        Assert.Equal(AiDecisionFallbackReason.MalformedProposal, outcome.FallbackReason);
        Assert.Null(outcome.Proposal);
        Assert.Null(outcome.Recommendation);
        Assert.Null(outcome.PolicyClassification);
    }

    [Fact]
    public void Policy_accepts_exact_thresholds_only_with_distinct_evidence()
    {
        var policy = Policy(minimumConfidence: 0.80m, minimumEvidenceCount: 2);
        var atThreshold = Proposal(
            confidence: 0.80m,
            evidence:
            [
                AiDecisionEvidence.Create("case-1", "workflow-input", EvidenceHash),
                AiDecisionEvidence.Create("case-2", "workflow-input", EvidenceHash),
            ]);
        var belowConfidence = Proposal(
            confidence: 0.79m,
            evidence: atThreshold.Evidence);
        var belowEvidence = Proposal(
            confidence: 0.80m,
            evidence: [atThreshold.Evidence[0]]);

        Assert.Equal(
            AiDecisionPolicyClassification.ShadowCandidate,
            policy.Evaluate(atThreshold).Classification);
        Assert.Equal(
            AiDecisionPolicyClassification.LowConfidence,
            policy.Evaluate(belowConfidence).Classification);
        Assert.Equal(
            AiDecisionPolicyClassification.EvidenceInsufficient,
            policy.Evaluate(belowEvidence).Classification);
    }

    [Theory]
    [InlineData("manual", "MODEL_INVENTED", false, true, 0.10, AiDecisionPolicyClassification.ManualRequested)]
    [InlineData("reject", "MODEL_INVENTED", false, true, 0.10, AiDecisionPolicyClassification.RejectRecommended)]
    [InlineData("approve", "MODEL_INVENTED", false, true, 0.10, AiDecisionPolicyClassification.DisallowedReason)]
    [InlineData("approve", "POLICY_MATCH", false, true, 0.10, AiDecisionPolicyClassification.EvidenceInsufficient)]
    [InlineData("approve", "POLICY_MATCH", true, true, 0.10, AiDecisionPolicyClassification.HighRisk)]
    [InlineData("approve", "POLICY_MATCH", true, false, 0.79, AiDecisionPolicyClassification.LowConfidence)]
    public void Policy_uses_the_documented_fail_closed_precedence(
        string recommendation,
        string reasonCode,
        bool includeEvidence,
        bool highRisk,
        double confidence,
        AiDecisionPolicyClassification expected)
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(
            recommendation: recommendation,
            confidence: Convert.ToDecimal(confidence),
            reasonCodes: [reasonCode],
            riskFlags: highRisk ? ["financial"] : [],
            evidence: includeEvidence ? [Evidence("case-1")] : []));
        var proposal = Assert.IsType<AiDecisionProposal>(parsed.Proposal);

        var evaluation = Policy(0.80m, 1).Evaluate(proposal);

        Assert.Equal(expected, evaluation.Classification);
        Assert.Equal(proposal.Recommendation, evaluation.Recommendation);
    }

    private static AiDecisionPolicyEvaluator Policy(
        decimal minimumConfidence,
        int minimumEvidenceCount) => new(new AiDecisionPolicyOptions
    {
        MinimumConfidence = minimumConfidence,
        MinimumEvidenceCount = minimumEvidenceCount,
        HighRiskFlags = ["financial"],
        AllowedReasonCodes = ["POLICY_MATCH"],
    });

    private static AiDecisionProposal Proposal(
        decimal confidence = 0.93m,
        IEnumerable<string>? reasonCodes = null,
        IEnumerable<AiDecisionEvidence>? evidence = null,
        IEnumerable<string>? riskFlags = null) => AiDecisionProposal.Create(
        AiDecisionProposalParser.SupportedSchemaVersion,
        AiDecisionRecommendation.Approve,
        confidence,
        reasonCodes ?? ["POLICY_MATCH"],
        "server policy boundary",
        evidence ?? [AiDecisionEvidence.Create("case-1", "workflow-input", EvidenceHash)],
        riskFlags ?? []);

    private static object Evidence(string id) => new
    {
        id,
        source = "workflow-input",
        contentHash = EvidenceHash,
    };

    private static string ProposalJson(
        string recommendation = "approve",
        decimal confidence = 0.93m,
        IEnumerable<string>? reasonCodes = null,
        IEnumerable<string>? riskFlags = null,
        IEnumerable<object>? evidence = null) => JsonSerializer.Serialize(new
    {
        schemaVersion = AiDecisionProposalParser.SupportedSchemaVersion,
        recommendation,
        confidence,
        reasonCodes = reasonCodes ?? ["POLICY_MATCH"],
        rationale = "server policy boundary",
        evidence = evidence ?? [Evidence("case-1")],
        riskFlags = riskFlags ?? [],
    });

    private static WfNodeExecutionContext Context() => new()
    {
        ExecutionKey = "ai-policy-boundary",
        InstanceId = 101,
        TokenId = 202,
        NodeId = "ai-node",
        NodeType = WfNodeType.AiDecision,
        DefinitionVersionId = 303,
        StarterUserId = 404,
        NodeProps = new WfNodeProps
        {
            AiInstructions = "Review the selected case identifier.",
            AiInputFields = ["caseId"],
        },
        VariablesJson = "{\"caseId\":\"case-1\"}",
        Attempt = 1,
        DeadlineAtUtc = new DateTimeOffset(2031, 1, 2, 3, 4, 5, TimeSpan.Zero),
    };

    private sealed class StaticAiDecisionProvider(AiDecisionProviderResult result) : IAiDecisionProvider
    {
        public Task<AiDecisionProviderResult> ProposeAsync(
            AiDecisionProviderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
