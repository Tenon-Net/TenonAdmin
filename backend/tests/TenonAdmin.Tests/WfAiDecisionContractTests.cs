using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M3b-0 Task 02 的纯契约红测：AI 只提交可审查 proposal，服务端分类，任何已完成的
/// provider 路径都必须停在人工兜底。这里刻意不涉及 HTTP wire、DI、发布枚举、tx2 或持久化。
/// </summary>
public class WfAiDecisionContractTests
{
    private const string ValidEvidenceDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ValidEvidenceHash = "sha256:" + ValidEvidenceDigest;
    private const string AuthorizationSecret = "Bearer tenon-test-ai-secret";

    [Fact]
    public void Provider_boundary_is_interface_backed_and_uses_a_typed_cancellable_operation()
    {
        Assert.True(typeof(IAiDecisionProvider).IsInterface);

        var operation = Assert.Single(typeof(IAiDecisionProvider).GetMethods());
        Assert.Equal("ProposeAsync", operation.Name);
        Assert.Equal(typeof(Task<AiDecisionProviderResult>), operation.ReturnType);

        Assert.Collection(
            operation.GetParameters(),
            parameter => Assert.Equal(typeof(AiDecisionProviderRequest), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public void External_parser_and_policy_SPI_can_construct_valid_typed_results()
    {
        var evidence = AiDecisionEvidence.Create("consumer-evidence", "consumer-source", ValidEvidenceHash);
        var proposal = AiDecisionProposal.Create(
            AiDecisionProposalParser.SupportedSchemaVersion,
            AiDecisionRecommendation.Approve,
            confidence: 0.92m,
            reasonCodes: ["POLICY_MATCH"],
            rationale: "consumer supplied proposal",
            evidence: [evidence],
            riskFlags: []);

        var valid = AiDecisionProposalParseResult.Valid(proposal);
        var invalid = AiDecisionProposalParseResult.Invalid();
        var evaluation = AiDecisionPolicyEvaluation.Create(
            proposal,
            AiDecisionPolicyClassification.HighRisk);

        Assert.True(valid.IsValid);
        Assert.Same(proposal, valid.Proposal);
        Assert.False(invalid.IsValid);
        Assert.Null(invalid.Proposal);
        Assert.Equal(AiDecisionPolicyClassification.HighRisk, evaluation.Classification);
        Assert.Throws<ArgumentException>(() =>
            AiDecisionEvidence.Create("consumer-evidence", "consumer-source", "sha256:invalid"));
        Assert.Throws<ArgumentException>(() => AiDecisionPolicyEvaluation.Create(
            proposal,
            AiDecisionPolicyClassification.RejectRecommended));
    }

    [Fact]
    public void Manual_fallback_null_argument_remains_source_compatible_and_public_factories_exclude_ai_outcomes()
    {
        var result = WfNodeExecutionResult.ManualFallback(null);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain(
            typeof(WfNodeExecutionResult).GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AiDecisionOutcome)));
    }

    [Fact]
    public void Handler_implements_the_ai_decision_node_handler_SPI()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Failed(null)));

        Assert.IsAssignableFrom<IWorkflowNodeHandler>(handler);
        Assert.Equal(WfNodeType.AiDecision, handler.NodeType);
    }

    [Fact]
    public async Task Handler_passes_execution_identity_attempt_and_deadline_to_its_injected_provider()
    {
        var provider = ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson()));
        var handler = NewHandler(provider);
        var context = Context(attempt: 2, businessKey: "ai-case-2");

        _ = await handler.ExecuteAsync(context, CancellationToken.None);

        var request = Assert.Single(provider.Requests);
        Assert.Equal(context.ExecutionKey, request.ExecutionKey);
        Assert.Equal(context.Attempt, request.Attempt);
        Assert.Equal(context.DeadlineAtUtc, request.DeadlineAtUtc);
        Assert.Equal(context.BusinessKey, request.BusinessKey);
    }

    [Fact]
    public async Task Handler_does_not_forward_raw_variables_json_to_the_provider_request()
    {
        const string rawSecret = "tenon-test-raw-variable-secret";
        var provider = ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson()));
        var handler = NewHandler(provider);
        var context = Context(orgId: 505, variablesJson: $"{{\"secret\":\"{rawSecret}\"}}");

        _ = await handler.ExecuteAsync(context, CancellationToken.None);

        var request = Assert.Single(provider.Requests);
        Assert.Equal(context.OrgId, request.OrgId);
        Assert.Null(typeof(AiDecisionProviderRequest).GetProperty(nameof(WfNodeExecutionContext.VariablesJson)));
        Assert.DoesNotContain(rawSecret, JsonSerializer.Serialize(request));
    }

    [Fact]
    public void Parser_accepts_the_complete_string_schema_version_1_0_proposal()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(riskFlags: ["financial"]));

        Assert.True(parsed.IsValid);
        var proposal = Assert.IsType<AiDecisionProposal>(parsed.Proposal);
        Assert.Equal("1.0", proposal.SchemaVersion);
        Assert.Equal(AiDecisionRecommendation.Approve, proposal.Recommendation);
        Assert.Equal(0.93m, proposal.Confidence);
        Assert.Equal(["POLICY_MATCH"], proposal.ReasonCodes);
        Assert.Equal("recommendation matches server policy", proposal.Rationale);
        Assert.Equal(["financial"], proposal.RiskFlags);

        var evidence = Assert.Single(proposal.Evidence);
        Assert.Equal("case-123", evidence.Id);
        Assert.Equal("workflow-input", evidence.Source);
        Assert.Equal(ValidEvidenceHash, evidence.ContentHash);
    }

    [Theory]
    [MemberData(nameof(SupportedRecommendations))]
    public void Parser_accepts_each_supported_recommendation(
        string recommendation,
        AiDecisionRecommendation expected)
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(recommendation: recommendation));

        Assert.True(parsed.IsValid);
        var proposal = Assert.IsType<AiDecisionProposal>(parsed.Proposal);
        Assert.Equal(expected, proposal.Recommendation);
    }

    public static IEnumerable<object[]> SupportedRecommendations()
    {
        yield return ["approve", AiDecisionRecommendation.Approve];
        yield return ["reject", AiDecisionRecommendation.Reject];
        yield return ["manual", AiDecisionRecommendation.Manual];
    }

    [Fact]
    public void Parser_rejects_an_unknown_root_property()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(includeUnexpectedProperty: true));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_a_duplicate_root_property()
    {
        var parsed = new AiDecisionProposalParser().Parse(
            ProposalJson()[..^1] + ",\"rationale\":\"duplicate rationale\"}");

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_a_proposal_missing_a_required_property()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(includeRationale: false));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Theory]
    [MemberData(nameof(RequiredRootProperties))]
    public void Parser_rejects_a_proposal_missing_each_required_root_property(string propertyName)
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJsonWithoutRootProperty(propertyName));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    public static IEnumerable<object[]> RequiredRootProperties()
    {
        yield return ["schemaVersion"];
        yield return ["recommendation"];
        yield return ["confidence"];
        yield return ["reasonCodes"];
        yield return ["rationale"];
        yield return ["evidence"];
        yield return ["riskFlags"];
    }

    [Theory]
    [MemberData(nameof(RequiredEvidenceProperties))]
    public void Parser_rejects_evidence_missing_each_required_property(string propertyName)
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJsonWithoutEvidenceProperty(propertyName));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    public static IEnumerable<object[]> RequiredEvidenceProperties()
    {
        yield return ["id"];
        yield return ["source"];
        yield return ["contentHash"];
    }

    [Fact]
    public void Parser_rejects_an_unknown_nested_evidence_property()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJsonWithUnknownEvidenceProperty());

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_a_duplicate_nested_evidence_property()
    {
        var json = ProposalJson().Replace(
            "\"id\":\"case-123\",",
            "\"id\":\"case-123\",\"id\":\"case-456\",",
            StringComparison.Ordinal);
        var parsed = new AiDecisionProposalParser().Parse(json);

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_an_unsupported_string_schema_version()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(schemaVersion: "2.0"));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_a_numeric_schema_version_even_when_the_value_is_one()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(rawSchemaVersion: "1"));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_a_command_like_recommendation()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(recommendation: "completeTask"));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_a_noncanonical_lowercase_reason_code()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(reasonCodes: ["policy_match"]));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_negative_confidence()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(confidence: -0.01m));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_confidence_above_one()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(confidence: 1.01m));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Theory]
    [MemberData(nameof(ClosedConfidenceBounds))]
    public void Parser_accepts_confidence_at_each_closed_range_bound(decimal confidence)
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(confidence: confidence));

        Assert.True(parsed.IsValid);
        var proposal = Assert.IsType<AiDecisionProposal>(parsed.Proposal);
        Assert.Equal(confidence, proposal.Confidence);
    }

    public static IEnumerable<object[]> ClosedConfidenceBounds()
    {
        yield return [0m];
        yield return [1m];
    }

    [Fact]
    public void Proposal_json_serializes_decimal_confidence_with_invariant_json_syntax_under_a_comma_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var json = ProposalJson(confidence: 0.93m);

            Assert.Contains("\"confidence\":0.93", json);
            Assert.True(new AiDecisionProposalParser().Parse(json).IsValid);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Parser_rejects_a_bare_evidence_digest_without_the_sha256_prefix()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(evidenceHash: ValidEvidenceDigest));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_an_evidence_digest_with_the_wrong_algorithm_prefix()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(evidenceHash: "sha512:" + ValidEvidenceDigest));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_a_malformed_sha256_evidence_digest()
    {
        var parsed = new AiDecisionProposalParser().Parse(ProposalJson(evidenceHash: "sha256:0123"));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_rejects_an_evidence_digest_with_uppercase_hex_characters()
    {
        var parsed = new AiDecisionProposalParser().Parse(
            ProposalJson(evidenceHash: "sha256:" + ValidEvidenceDigest.ToUpperInvariant()));

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Proposal);
    }

    [Fact]
    public void Parser_enforces_the_total_json_character_limit_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = ProposalJsonAtTotalLength(AiDecisionProposalParser.MaximumJsonCharacters);
        var aboveLimit = ProposalJsonAtTotalLength(AiDecisionProposalParser.MaximumJsonCharacters + 1);

        Assert.Equal(AiDecisionProposalParser.MaximumJsonCharacters, atLimit.Length);
        Assert.True(parser.Parse(atLimit).IsValid);
        Assert.False(parser.Parse(aboveLimit).IsValid);
    }

    [Fact]
    public void Parser_enforces_the_rationale_length_limit_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = new string('r', AiDecisionProposalParser.MaximumRationaleCharacters);
        var aboveLimit = new string('r', AiDecisionProposalParser.MaximumRationaleCharacters + 1);

        Assert.True(parser.Parse(ProposalJson(rationale: atLimit)).IsValid);
        Assert.False(parser.Parse(ProposalJson(rationale: aboveLimit)).IsValid);
    }

    [Fact]
    public void Parser_enforces_reason_code_cardinality_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = Enumerable.Range(0, AiDecisionProposalParser.MaximumReasonCodeCount)
            .Select(index => $"REASON_{index}")
            .ToArray();
        var aboveLimit = Enumerable.Range(0, AiDecisionProposalParser.MaximumReasonCodeCount + 1)
            .Select(index => $"REASON_{index}")
            .ToArray();

        Assert.True(parser.Parse(ProposalJson(reasonCodes: atLimit)).IsValid);
        Assert.False(parser.Parse(ProposalJson(reasonCodes: aboveLimit)).IsValid);
        Assert.False(parser.Parse(ProposalJson(reasonCodes: [])).IsValid);
    }

    [Fact]
    public void Parser_enforces_risk_flag_cardinality_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = Enumerable.Range(0, AiDecisionProposalParser.MaximumRiskFlagCount)
            .Select(index => $"risk-{index}")
            .ToArray();
        var aboveLimit = Enumerable.Range(0, AiDecisionProposalParser.MaximumRiskFlagCount + 1)
            .Select(index => $"risk-{index}")
            .ToArray();

        Assert.True(parser.Parse(ProposalJson(riskFlags: atLimit)).IsValid);
        Assert.False(parser.Parse(ProposalJson(riskFlags: aboveLimit)).IsValid);
    }

    [Fact]
    public void Parser_enforces_evidence_cardinality_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();

        Assert.True(parser.Parse(ProposalJson(evidenceCount: AiDecisionProposalParser.MaximumEvidenceCount)).IsValid);
        Assert.False(parser.Parse(ProposalJson(evidenceCount: AiDecisionProposalParser.MaximumEvidenceCount + 1)).IsValid);
    }

    [Fact]
    public void Parser_enforces_reason_code_item_length_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = new string('A', AiDecisionProposalParser.MaximumReasonCodeCharacters);
        var aboveLimit = new string('A', AiDecisionProposalParser.MaximumReasonCodeCharacters + 1);

        Assert.True(parser.Parse(ProposalJson(reasonCodes: [atLimit])).IsValid);
        Assert.False(parser.Parse(ProposalJson(reasonCodes: [aboveLimit])).IsValid);
    }

    [Fact]
    public void Parser_enforces_risk_flag_item_length_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = new string('r', AiDecisionProposalParser.MaximumRiskFlagCharacters);
        var aboveLimit = new string('r', AiDecisionProposalParser.MaximumRiskFlagCharacters + 1);

        Assert.True(parser.Parse(ProposalJson(riskFlags: [atLimit])).IsValid);
        Assert.False(parser.Parse(ProposalJson(riskFlags: [aboveLimit])).IsValid);
    }

    [Fact]
    public void Parser_enforces_evidence_id_item_length_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = new string('i', AiDecisionProposalParser.MaximumEvidenceIdCharacters);
        var aboveLimit = new string('i', AiDecisionProposalParser.MaximumEvidenceIdCharacters + 1);

        Assert.True(parser.Parse(ProposalJson(evidenceId: atLimit)).IsValid);
        Assert.False(parser.Parse(ProposalJson(evidenceId: aboveLimit)).IsValid);
    }

    [Fact]
    public void Parser_enforces_evidence_source_item_length_at_each_boundary()
    {
        var parser = new AiDecisionProposalParser();
        var atLimit = new string('s', AiDecisionProposalParser.MaximumEvidenceSourceCharacters);
        var aboveLimit = new string('s', AiDecisionProposalParser.MaximumEvidenceSourceCharacters + 1);

        Assert.True(parser.Parse(ProposalJson(evidenceSource: atLimit)).IsValid);
        Assert.False(parser.Parse(ProposalJson(evidenceSource: aboveLimit)).IsValid);
    }

    [Fact]
    public void Policy_options_reject_invalid_ranges_and_noncanonical_reason_codes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions
        {
            MinimumConfidence = -0.01m,
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions
        {
            MinimumEvidenceCount = AiDecisionProposalParser.MaximumEvidenceCount + 1,
        }));
        Assert.Throws<ArgumentException>(() => new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions
        {
            AllowedReasonCodes = ["policy_match"],
        }));
    }

    [Fact]
    public void Policy_marks_a_complete_low_risk_proposal_as_a_shadow_candidate()
    {
        var classification = NewPolicy().Evaluate(ParseProposal(ProposalJson()));

        Assert.Equal(AiDecisionPolicyClassification.ShadowCandidate, classification.Classification);
    }

    [Fact]
    public void Policy_marks_a_reject_recommendation_as_reject_recommended()
    {
        var classification = NewPolicy().Evaluate(ParseProposal(ProposalJson(recommendation: "reject")));

        Assert.Equal(AiDecisionPolicyClassification.RejectRecommended, classification.Classification);
    }

    [Fact]
    public void Policy_marks_a_manual_recommendation_as_manual_requested()
    {
        var classification = NewPolicy().Evaluate(ParseProposal(ProposalJson(recommendation: "manual")));

        Assert.Equal(AiDecisionPolicyClassification.ManualRequested, classification.Classification);
    }

    [Fact]
    public void Policy_marks_a_below_threshold_proposal_as_low_confidence()
    {
        var classification = NewPolicy().Evaluate(ParseProposal(ProposalJson(confidence: 0.79m)));

        Assert.Equal(AiDecisionPolicyClassification.LowConfidence, classification.Classification);
    }

    [Fact]
    public void Policy_marks_a_proposal_with_a_configured_risk_flag_as_high_risk()
    {
        var classification = NewPolicy().Evaluate(ParseProposal(ProposalJson(riskFlags: ["financial"])));

        Assert.Equal(AiDecisionPolicyClassification.HighRisk, classification.Classification);
    }

    [Fact]
    public void Policy_marks_a_proposal_without_evidence_as_evidence_insufficient()
    {
        var classification = NewPolicy().Evaluate(ParseProposal(ProposalJson(includeEvidence: false)));

        Assert.Equal(AiDecisionPolicyClassification.EvidenceInsufficient, classification.Classification);
    }

    [Fact]
    public void Policy_marks_a_proposal_with_an_unallowed_reason_code_as_disallowed_reason()
    {
        var classification = NewPolicy().Evaluate(ParseProposal(ProposalJson(reasonCodes: ["MODEL_INVENTED_REASON"])));

        Assert.Equal(AiDecisionPolicyClassification.DisallowedReason, classification.Classification);
    }

    [Fact]
    public void Policy_classification_is_deterministic_for_the_same_proposal()
    {
        var policy = NewPolicy();
        var proposal = ParseProposal(ProposalJson(riskFlags: ["financial"]));

        var first = policy.Evaluate(proposal);
        var second = policy.Evaluate(proposal);

        Assert.Equal(first.Classification, second.Classification);
    }

    [Fact]
    public async Task An_approve_recommendation_still_routes_to_manual_fallback()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson(recommendation: "approve"))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(
            result,
            AiDecisionRecommendation.Approve,
            AiDecisionPolicyClassification.ShadowCandidate,
            AiDecisionFallbackReason.ShadowOnly);
    }

    [Fact]
    public async Task A_reject_recommendation_still_routes_to_manual_fallback_without_rejecting_the_workflow()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson(recommendation: "reject"))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(
            result,
            AiDecisionRecommendation.Reject,
            AiDecisionPolicyClassification.RejectRecommended,
            AiDecisionFallbackReason.RejectNotAllowed);
    }

    [Fact]
    public async Task A_manual_recommendation_still_routes_to_manual_fallback_with_its_requested_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson(recommendation: "manual"))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(
            result,
            AiDecisionRecommendation.Manual,
            AiDecisionPolicyClassification.ManualRequested,
            AiDecisionFallbackReason.ManualRequested);
    }

    [Fact]
    public async Task A_low_confidence_proposal_still_routes_to_manual_fallback_with_its_typed_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson(confidence: 0.79m))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(
            result,
            AiDecisionRecommendation.Approve,
            AiDecisionPolicyClassification.LowConfidence,
            AiDecisionFallbackReason.LowConfidence);
    }

    [Fact]
    public async Task A_high_risk_proposal_still_routes_to_manual_fallback_with_its_typed_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson(riskFlags: ["financial"]))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(
            result,
            AiDecisionRecommendation.Approve,
            AiDecisionPolicyClassification.HighRisk,
            AiDecisionFallbackReason.HighRisk);
    }

    [Fact]
    public async Task An_evidence_insufficient_proposal_still_routes_to_manual_fallback_with_its_typed_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson(includeEvidence: false))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(
            result,
            AiDecisionRecommendation.Approve,
            AiDecisionPolicyClassification.EvidenceInsufficient,
            AiDecisionFallbackReason.EvidenceInsufficient);
    }

    [Fact]
    public async Task A_proposal_with_a_disallowed_reason_still_routes_to_manual_fallback_with_its_typed_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(
            ProposalJson(reasonCodes: ["MODEL_INVENTED_REASON"]))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(
            result,
            AiDecisionRecommendation.Approve,
            AiDecisionPolicyClassification.DisallowedReason,
            AiDecisionFallbackReason.DisallowedReason);
    }

    [Fact]
    public async Task A_malformed_proposal_still_routes_to_manual_fallback_with_its_typed_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal("{not json")));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(result, null, null, AiDecisionFallbackReason.MalformedProposal);
    }

    [Fact]
    public async Task A_command_like_recommendation_still_routes_to_manual_fallback_with_its_typed_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Proposal(
            ProposalJson(recommendation: "completeTask"))));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(result, null, null, AiDecisionFallbackReason.MalformedProposal);
    }

    [Fact]
    public async Task A_provider_timeout_still_routes_to_manual_fallback_with_its_typed_reason()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.TimedOut()));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        AssertManualFallback(result, null, null, AiDecisionFallbackReason.ProviderTimeout);
    }

    [Fact]
    public async Task A_typed_provider_failure_routes_to_manual_fallback_and_redacts_the_authorization_secret()
    {
        var handler = NewHandler(ProviderReturning(AiDecisionProviderResult.Failed(
            $"Authorization: {AuthorizationSecret}")));

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        var outcome = AssertManualFallback(result, null, null, AiDecisionFallbackReason.ProviderFailure);
        AssertNoAuthorizationSecret(result, outcome);
    }

    [Fact]
    public async Task A_thrown_non_cancellation_provider_failure_routes_to_manual_fallback_and_redacts_the_authorization_secret()
    {
        var provider = new RecordingAiDecisionProvider((_, _) =>
            Task.FromException<AiDecisionProviderResult>(
                new InvalidOperationException($"Authorization: {AuthorizationSecret}")));
        var handler = NewHandler(provider);

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        var outcome = AssertManualFallback(result, null, null, AiDecisionFallbackReason.ProviderFailure);
        AssertNoAuthorizationSecret(result, outcome);
    }

    [Fact]
    public async Task Parser_and_policy_failures_are_not_misclassified_as_provider_failures()
    {
        var provider = ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson()));
        var parserFailure = new AiDecisionNodeHandler(provider, new ThrowingParser(), NewPolicy());
        var policyFailure = new AiDecisionNodeHandler(provider, new AiDecisionProposalParser(), new ThrowingPolicy());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            parserFailure.ExecuteAsync(Context(), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policyFailure.ExecuteAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task A_pre_cancelled_token_prevents_the_provider_call_and_propagates_cancellation()
    {
        var provider = ProviderReturning(AiDecisionProviderResult.Proposal(ProposalJson()));
        var handler = NewHandler(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() => handler.ExecuteAsync(Context(), cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task External_cancellation_propagates_without_becoming_a_manual_result()
    {
        var provider = new RecordingAiDecisionProvider((_, cancellationToken) => WaitForCancellationAsync(cancellationToken));
        var handler = NewHandler(provider);
        using var cancellation = new CancellationTokenSource();

        var running = handler.ExecuteAsync(Context(), cancellation.Token);
        await provider.InvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        var exception = await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        var cancellationException = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancellationException.CancellationToken);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task External_cancellation_after_a_provider_ignores_it_still_propagates_without_a_manual_result()
    {
        var provider = new CancellationIgnoringAiDecisionProvider(AiDecisionProviderResult.Proposal(ProposalJson()));
        var handler = NewHandler(provider);
        using var cancellation = new CancellationTokenSource();

        var running = handler.ExecuteAsync(Context(), cancellation.Token);
        await provider.InvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        provider.Complete();

        var exception = await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        var cancellationException = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancellationException.CancellationToken);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void Handler_declares_no_database_session_or_workflow_state_dependencies()
    {
        var dependencies = DeclaredDependencies(typeof(AiDecisionNodeHandler));
        Assert.NotEmpty(dependencies);

        foreach (var dependency in dependencies)
        {
            Assert.False(
                ContainsForbiddenDependency(dependency.Type, new HashSet<Type>()),
                $"{dependency.Name} ({dependency.Type.FullName}) lets AiDecisionNodeHandler access a database/session or workflow state.");
        }
    }

    [Fact]
    public void Handler_dependency_guard_recurses_into_array_element_types()
    {
        Assert.True(ContainsForbiddenDependency(typeof(ISqlSugarClient[]), new HashSet<Type>()));
    }

    private static AiDecisionNodeHandler NewHandler(IAiDecisionProvider provider) => new(
        provider,
        new AiDecisionProposalParser(),
        NewPolicy());

    private static AiDecisionPolicyEvaluator NewPolicy() => new(new AiDecisionPolicyOptions
    {
        MinimumConfidence = 0.80m,
        MinimumEvidenceCount = 1,
        HighRiskFlags = ["financial", "legal"],
        AllowedReasonCodes = ["POLICY_MATCH", "MANUAL_CHECK"],
    });

    private static AiDecisionProposal ParseProposal(string json)
    {
        var parsed = new AiDecisionProposalParser().Parse(json);
        Assert.True(parsed.IsValid);
        return Assert.IsType<AiDecisionProposal>(parsed.Proposal);
    }

    private static AiDecisionOutcome AssertManualFallback(
        WfNodeExecutionResult result,
        AiDecisionRecommendation? expectedRecommendation,
        AiDecisionPolicyClassification? expectedClassification,
        AiDecisionFallbackReason expectedFallbackReason)
    {
        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        var outcome = Assert.IsType<AiDecisionOutcome>(result.AiDecision);
        Assert.Equal(expectedRecommendation, outcome.Recommendation);
        Assert.Equal(expectedClassification, outcome.PolicyClassification);
        Assert.Equal(expectedFallbackReason, outcome.FallbackReason);
        return outcome;
    }

    private static void AssertNoAuthorizationSecret(WfNodeExecutionResult result, AiDecisionOutcome outcome)
    {
        Assert.DoesNotContain(AuthorizationSecret, result.Summary ?? string.Empty);
        Assert.DoesNotContain(AuthorizationSecret, result.OutputJson ?? string.Empty);
        Assert.DoesNotContain(AuthorizationSecret, JsonSerializer.Serialize(outcome));
    }

    private static RecordingAiDecisionProvider ProviderReturning(AiDecisionProviderResult result) =>
        new((_, _) => Task.FromResult(result));

    private static async Task<AiDecisionProviderResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The provider call completed without external cancellation.");
    }

    // Task 06 已交付真实 AI 节点键；AI 专属 fixture 不得再借用 Webhook 上下文。
    private static WfNodeExecutionContext Context(
        int attempt = 1,
        long? orgId = null,
        string? variablesJson = null,
        string? businessKey = null) => new()
    {
        ExecutionKey = "ai-exec-1",
        InstanceId = 101,
        TokenId = 202,
        NodeId = "ai-node",
        NodeType = WfNodeType.AiDecision,
        DefinitionVersionId = 303,
        OrgId = orgId,
        StarterUserId = 404,
        BusinessKey = businessKey,
        NodeProps = new WfNodeProps
        {
            AiInstructions = "Review the selected case identifier.",
            AiInputFields = ["caseId"],
        },
        Attempt = attempt,
        DeadlineAtUtc = new DateTimeOffset(2031, 1, 2, 3, 4, 5, TimeSpan.Zero),
        VariablesJson = variablesJson ?? "{\"caseId\":\"case-123\"}",
    };

    private static string ProposalJson(
        string schemaVersion = "1.0",
        string? rawSchemaVersion = null,
        string recommendation = "approve",
        decimal confidence = 0.93m,
        string[]? reasonCodes = null,
        string[]? riskFlags = null,
        bool includeRationale = true,
        bool includeUnexpectedProperty = false,
        string evidenceHash = ValidEvidenceHash,
        bool includeEvidence = true,
        string? rationale = null,
        int evidenceCount = 1,
        string? evidenceId = null,
        string? evidenceSource = null)
    {
        var properties = new List<string>
        {
            "\"schemaVersion\":" + (rawSchemaVersion ?? JsonSerializer.Serialize(schemaVersion)),
            "\"recommendation\":" + JsonSerializer.Serialize(recommendation),
            "\"confidence\":" + JsonSerializer.Serialize(confidence),
            "\"reasonCodes\":" + JsonSerializer.Serialize(reasonCodes ?? ["POLICY_MATCH"]),
        };

        if (includeRationale)
        {
            properties.Add("\"rationale\":" + JsonSerializer.Serialize(rationale ?? "recommendation matches server policy"));
        }

        properties.Add("\"evidence\":" + (includeEvidence
            ? EvidenceJson(evidenceHash, evidenceCount, evidenceId, evidenceSource)
            : "[]"));
        properties.Add("\"riskFlags\":" + JsonSerializer.Serialize(riskFlags ?? []));

        if (includeUnexpectedProperty)
        {
            properties.Add("\"unexpected\":true");
        }

        return "{" + string.Join(',', properties) + "}";
    }

    private static string EvidenceJson(
        string contentHash,
        int count = 1,
        string? id = null,
        string? source = null) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(index =>
            "{\"id\":" + JsonSerializer.Serialize(index == 0 ? id ?? "case-123" : $"case-{index}") +
            ",\"source\":" + JsonSerializer.Serialize(index == 0 ? source ?? "workflow-input" : "workflow-input") +
            ",\"contentHash\":" + JsonSerializer.Serialize(contentHash) + "}")) + "]";

    private static string ProposalJsonAtTotalLength(int length)
    {
        var json = ProposalJson();
        if (json.Length > length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The requested length is shorter than a valid proposal.");
        }

        return json[..^1] + new string(' ', length - json.Length) + "}";
    }

    private static string ProposalJsonWithoutRootProperty(string propertyName)
    {
        var root = ProposalJsonObject();
        if (!root.Remove(propertyName))
        {
            throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Expected a required root property.");
        }

        return root.ToJsonString();
    }

    private static string ProposalJsonWithoutEvidenceProperty(string propertyName)
    {
        var evidence = FirstEvidenceObject();
        if (!evidence.Remove(propertyName))
        {
            throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Expected a required evidence property.");
        }

        return evidence.Parent!.Parent!.ToJsonString();
    }

    private static string ProposalJsonWithUnknownEvidenceProperty()
    {
        var evidence = FirstEvidenceObject();
        evidence["unexpected"] = JsonValue.Create(true);
        return evidence.Parent!.Parent!.ToJsonString();
    }

    private static JsonObject FirstEvidenceObject()
    {
        var root = ProposalJsonObject();
        return root["evidence"]!.AsArray()[0]!.AsObject();
    }

    private static JsonObject ProposalJsonObject() => JsonNode.Parse(ProposalJson())!.AsObject();

    private static IEnumerable<(string Name, Type Type)> DeclaredDependencies(Type handlerType)
    {
        foreach (var constructor in handlerType.GetConstructors(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return ($"constructor parameter {parameter.Name}", parameter.ParameterType);
            }
        }

        foreach (var field in handlerType.GetFields(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return ($"field {field.Name}", field.FieldType);
        }

        foreach (var property in handlerType.GetProperties(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return ($"property {property.Name}", property.PropertyType);
        }
    }

    private static bool ContainsForbiddenDependency(Type rawType, HashSet<Type> visited)
    {
        var type = Nullable.GetUnderlyingType(rawType) ?? rawType;
        if (!visited.Add(type))
        {
            return false;
        }

        if (typeof(IServiceProvider).IsAssignableFrom(type)
            || typeof(IServiceScopeFactory).IsAssignableFrom(type)
            || typeof(ISqlSugarClient).IsAssignableFrom(type)
            || typeof(IWorkflowEngine).IsAssignableFrom(type)
            || typeof(WfTask).IsAssignableFrom(type)
            || typeof(WfToken).IsAssignableFrom(type)
            || typeof(PrimaryId).IsAssignableFrom(type)
            || type.Namespace?.StartsWith("SqlSugar", StringComparison.Ordinal) == true)
        {
            return true;
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsForbiddenDependency(elementType, visited);
        }

        return type.IsGenericType
               && type.GetGenericArguments().Any(argument => ContainsForbiddenDependency(argument, visited));
    }

    private sealed class CancellationIgnoringAiDecisionProvider(AiDecisionProviderResult result) : IAiDecisionProvider
    {
        private readonly TaskCompletionSource<AiDecisionProviderResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public TaskCompletionSource<bool> InvocationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AiDecisionProviderResult> ProposeAsync(
            AiDecisionProviderRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            InvocationStarted.TrySetResult(true);
            return _completion.Task;
        }

        public void Complete() => _completion.TrySetResult(result);
    }

    private sealed class RecordingAiDecisionProvider(
        Func<AiDecisionProviderRequest, CancellationToken, Task<AiDecisionProviderResult>> propose)
        : IAiDecisionProvider
    {
        private readonly Func<AiDecisionProviderRequest, CancellationToken, Task<AiDecisionProviderResult>> _propose = propose;

        public List<AiDecisionProviderRequest> Requests { get; } = [];

        public int CallCount { get; private set; }

        public TaskCompletionSource<bool> InvocationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AiDecisionProviderResult> ProposeAsync(
            AiDecisionProviderRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
            InvocationStarted.TrySetResult(true);
            return _propose(request, cancellationToken);
        }
    }

    private sealed class ThrowingParser : IAiDecisionProposalParser
    {
        public AiDecisionProposalParseResult Parse(string? json) =>
            throw new InvalidOperationException("parser contract failure");
    }

    private sealed class ThrowingPolicy : IAiDecisionPolicyEvaluator
    {
        public AiDecisionPolicyEvaluation Evaluate(AiDecisionProposal proposal) =>
            throw new InvalidOperationException("policy contract failure");
    }
}
