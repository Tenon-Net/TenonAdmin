using System.Reflection;
using System.Text.Json;
using SqlSugar;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M3b-0 Task 04 的确定性 Fake Provider 契约：它仅供消费者和测试前置注入，默认 DI 仍保持 fail-closed。
/// </summary>
public class WfFakeAiDecisionProviderTests
{
    private const string EvidenceHash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Rationale = "Deterministic fake proposal matches the configured policy.";
    private const string ThrowsMessage = "The deterministic fake AI decision provider failed.";
    private const string SuccessProposalJson =
        "{\"schemaVersion\":\"1.0\",\"recommendation\":\"approve\",\"confidence\":0.95,\"reasonCodes\":[\"POLICY_MATCH\"],\"rationale\":\"Deterministic fake proposal matches the configured policy.\",\"evidence\":[{\"id\":\"fake-evidence\",\"source\":\"fake-provider\",\"contentHash\":\"sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}],\"riskFlags\":[]}";

    [Fact]
    public async Task Parameterless_success_scenario_returns_byte_identical_canonical_proposals()
    {
        var provider = new FakeAiDecisionProvider();

        var first = await provider.ProposeAsync(Request(), CancellationToken.None);
        var second = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Proposal, first.Type);
        Assert.Equal(SuccessProposalJson, first.ProposalJson);
        Assert.Equal(first.ProposalJson, second.ProposalJson);
        Assert.Equal(2, provider.CallCount);

        using var document = JsonDocument.Parse(Assert.IsType<string>(first.ProposalJson));
        var proposal = document.RootElement;
        Assert.Equal("1.0", proposal.GetProperty("schemaVersion").GetString());
        Assert.Equal("approve", proposal.GetProperty("recommendation").GetString());
        Assert.Equal(0.95m, proposal.GetProperty("confidence").GetDecimal());
        Assert.Equal(["POLICY_MATCH"], proposal.GetProperty("reasonCodes").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(Rationale, proposal.GetProperty("rationale").GetString());
        Assert.InRange(proposal.GetProperty("rationale").GetString()!.Length, 1, AiDecisionProposalParser.MaximumRationaleCharacters);
        var evidence = proposal.GetProperty("evidence").EnumerateArray().Single();
        Assert.Equal("fake-evidence", evidence.GetProperty("id").GetString());
        Assert.Equal("fake-provider", evidence.GetProperty("source").GetString());
        Assert.Equal(EvidenceHash, evidence.GetProperty("contentHash").GetString());
        Assert.Equal(0, proposal.GetProperty("riskFlags").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(DirectProviderScenarios))]
    public async Task Explicit_scenarios_return_their_typed_direct_results(
        FakeAiDecisionScenario scenario,
        AiDecisionProviderResultType? expectedType,
        string? expectedProposalJson,
        string? expectedExceptionMessage)
    {
        var provider = new FakeAiDecisionProvider(scenario);

        if (expectedExceptionMessage is not null)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.ProposeAsync(Request(), CancellationToken.None));

            Assert.Equal(expectedExceptionMessage, exception.Message);
            Assert.Equal(1, provider.CallCount);
            return;
        }

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(expectedType, result.Type);
        Assert.Equal(expectedProposalJson, result.ProposalJson);
        Assert.Equal(1, provider.CallCount);
    }

    public static IEnumerable<object?[]> DirectProviderScenarios()
    {
        yield return [FakeAiDecisionScenario.Success, AiDecisionProviderResultType.Proposal, SuccessProposalJson, null];
        yield return [FakeAiDecisionScenario.MalformedProposal, AiDecisionProviderResultType.Proposal, "{\"schemaVersion\":\"1.0\"}", null];
        yield return [FakeAiDecisionScenario.LowConfidence, AiDecisionProviderResultType.Proposal,
            SuccessProposalJson.Replace("\"confidence\":0.95", "\"confidence\":0.79", StringComparison.Ordinal), null];
        yield return [FakeAiDecisionScenario.HighRisk, AiDecisionProviderResultType.Proposal,
            SuccessProposalJson.Replace("\"riskFlags\":[]", $"\"riskFlags\":[\"{FakeAiDecisionProvider.HighRiskFlag}\"]", StringComparison.Ordinal), null];
        yield return [FakeAiDecisionScenario.TimedOut, AiDecisionProviderResultType.TimedOut, null, null];
        yield return [FakeAiDecisionScenario.Failed, AiDecisionProviderResultType.Failed, null, null];
        yield return [FakeAiDecisionScenario.Throws, null, null, ThrowsMessage];
    }

    [Fact]
    public async Task Low_confidence_payload_differs_from_success_only_by_its_confidence()
    {
        var success = await new FakeAiDecisionProvider(FakeAiDecisionScenario.Success)
            .ProposeAsync(Request(), CancellationToken.None);
        var lowConfidence = await new FakeAiDecisionProvider(FakeAiDecisionScenario.LowConfidence)
            .ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(
            Assert.IsType<string>(success.ProposalJson).Replace("\"confidence\":0.95", "\"confidence\":0.79", StringComparison.Ordinal),
            lowConfidence.ProposalJson);
    }

    [Theory]
    [InlineData(FakeAiDecisionScenario.Success, AiDecisionPolicyClassification.ShadowCandidate)]
    [InlineData(FakeAiDecisionScenario.LowConfidence, AiDecisionPolicyClassification.LowConfidence)]
    [InlineData(FakeAiDecisionScenario.HighRisk, AiDecisionPolicyClassification.HighRisk)]
    public async Task Parsed_proposals_receive_the_expected_server_policy_classification(
        FakeAiDecisionScenario scenario,
        AiDecisionPolicyClassification expectedClassification)
    {
        var provider = new FakeAiDecisionProvider(scenario);
        var result = await provider.ProposeAsync(Request(), CancellationToken.None);
        var parsed = new AiDecisionProposalParser().Parse(result.ProposalJson);

        Assert.True(parsed.IsValid);
        var proposal = Assert.IsType<AiDecisionProposal>(parsed.Proposal);
        var evaluation = NewPolicy().Evaluate(proposal);

        Assert.Equal(expectedClassification, evaluation.Classification);
    }

    [Theory]
    [MemberData(nameof(HandlerScenarios))]
    public async Task Every_scenario_returns_manual_fallback_with_its_typed_reason(
        FakeAiDecisionScenario scenario,
        AiDecisionFallbackReason expectedFallbackReason,
        AiDecisionRecommendation? expectedRecommendation,
        AiDecisionPolicyClassification? expectedClassification)
    {
        var provider = new FakeAiDecisionProvider(scenario);
        var result = await NewHandler(provider).ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        var outcome = Assert.IsType<AiDecisionOutcome>(result.AiDecision);
        Assert.Equal(expectedFallbackReason, outcome.FallbackReason);
        Assert.Equal(expectedRecommendation, outcome.Recommendation);
        Assert.Equal(expectedClassification, outcome.PolicyClassification);
        Assert.Equal(1, provider.CallCount);
    }

    public static IEnumerable<object?[]> HandlerScenarios()
    {
        yield return [FakeAiDecisionScenario.Success, AiDecisionFallbackReason.ShadowOnly,
            AiDecisionRecommendation.Approve, AiDecisionPolicyClassification.ShadowCandidate];
        yield return [FakeAiDecisionScenario.MalformedProposal, AiDecisionFallbackReason.MalformedProposal, null, null];
        yield return [FakeAiDecisionScenario.LowConfidence, AiDecisionFallbackReason.LowConfidence,
            AiDecisionRecommendation.Approve, AiDecisionPolicyClassification.LowConfidence];
        yield return [FakeAiDecisionScenario.HighRisk, AiDecisionFallbackReason.HighRisk,
            AiDecisionRecommendation.Approve, AiDecisionPolicyClassification.HighRisk];
        yield return [FakeAiDecisionScenario.TimedOut, AiDecisionFallbackReason.ProviderTimeout, null, null];
        yield return [FakeAiDecisionScenario.Failed, AiDecisionFallbackReason.ProviderFailure, null, null];
        yield return [FakeAiDecisionScenario.Throws, AiDecisionFallbackReason.ProviderFailure, null, null];
    }

    [Fact]
    public async Task Thrown_scenario_is_redacted_by_the_handler()
    {
        var result = await NewHandler(new FakeAiDecisionProvider(FakeAiDecisionScenario.Throws))
            .ExecuteAsync(Context(), CancellationToken.None);
        var outcome = Assert.IsType<AiDecisionOutcome>(result.AiDecision);
        var serializedOutcome = JsonSerializer.Serialize(outcome);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Equal(AiDecisionFallbackReason.ProviderFailure, outcome.FallbackReason);
        Assert.Equal("AI 决策服务不可用，已转人工处理。", result.Summary);
        Assert.Null(result.OutputJson);
        Assert.DoesNotContain(ThrowsMessage, result.Summary ?? string.Empty);
        Assert.DoesNotContain(ThrowsMessage, result.OutputJson ?? string.Empty);
        Assert.DoesNotContain(ThrowsMessage, serializedOutcome);
    }

    [Fact]
    public async Task Pre_cancelled_calls_produce_no_output_and_do_not_increment_call_count()
    {
        var provider = new FakeAiDecisionProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() => provider.ProposeAsync(Request(), cancellation.Token));

        var cancellationException = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancellationException.CancellationToken);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task A_valid_call_increments_the_thread_safe_counter_once()
    {
        var provider = new FakeAiDecisionProvider();

        _ = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void Invalid_scenarios_are_rejected_at_construction(int value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new FakeAiDecisionProvider((FakeAiDecisionScenario)value));

        Assert.Equal("scenario", exception.ParamName);
    }

    [Fact]
    public void Provider_does_not_retain_requests_or_depend_on_database_or_network_types()
    {
        var providerType = typeof(FakeAiDecisionProvider);
        var storedTypes = providerType
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(field => field.FieldType)
            .Concat(providerType
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(property => property.PropertyType));

        Assert.All(storedTypes, type => Assert.False(IsForbiddenStoredDependency(type)));
        Assert.All(
            providerType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType),
            type => Assert.False(IsForbiddenStoredDependency(type)));
    }

    private static AiDecisionNodeHandler NewHandler(IAiDecisionProvider provider) => new(
        provider,
        new AiDecisionProposalParser(),
        NewPolicy());

    private static AiDecisionPolicyEvaluator NewPolicy() => new(new AiDecisionPolicyOptions
    {
        MinimumConfidence = 0.80m,
        MinimumEvidenceCount = 1,
        HighRiskFlags = [FakeAiDecisionProvider.HighRiskFlag],
        AllowedReasonCodes = ["POLICY_MATCH"],
    });

    private static AiDecisionProviderRequest Request() => new()
    {
        ExecutionKey = "fake-ai-execution",
        InstanceId = 101,
        TokenId = 202,
        NodeId = "ai-node",
        DefinitionVersionId = 303,
        StarterUserId = 404,
        Attempt = 1,
        DeadlineAtUtc = new DateTimeOffset(2031, 1, 2, 3, 4, 5, TimeSpan.Zero),
    };

    private static WfNodeExecutionContext Context() => new()
    {
        ExecutionKey = "fake-ai-execution",
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
        VariablesJson = "{\"caseId\":\"case-123\"}",
        Attempt = 1,
        DeadlineAtUtc = new DateTimeOffset(2031, 1, 2, 3, 4, 5, TimeSpan.Zero),
    };

    private static bool IsForbiddenStoredDependency(Type type)
    {
        if (type == typeof(AiDecisionProviderRequest)
            || type == typeof(HttpClient)
            || type == typeof(HttpMessageHandler)
            || typeof(IServiceProvider).IsAssignableFrom(type)
            || typeof(ISqlSugarClient).IsAssignableFrom(type)
            || type.Namespace?.StartsWith("SqlSugar", StringComparison.Ordinal) == true)
        {
            return true;
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            return elementType is not null && IsForbiddenStoredDependency(elementType);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsForbiddenStoredDependency);
    }
}
