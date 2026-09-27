using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>M3b-0 AI 装配契约：Provider 可替换，AI handler 进入既有 dispatcher 集合。</summary>
public class WfAiDecisionRegistrationTests
{
    private const string EvidenceHash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Default_provider_resolves_fails_closed_and_respects_cancellation()
    {
        var services = new ServiceCollection();
        services.AddTenonAdminWorkflow();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var aiProvider = scope.ServiceProvider.GetRequiredService<IAiDecisionProvider>();

        Assert.IsType<FailClosedAiDecisionProvider>(aiProvider);

        var result = await aiProvider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Failed, result.Type);
        Assert.Null(result.ProposalJson);

        var invalidRequestException = await Record.ExceptionAsync(
            () => aiProvider.ProposeAsync(Request(executionKey: ""), CancellationToken.None));

        Assert.IsAssignableFrom<ArgumentException>(invalidRequestException);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() => aiProvider.ProposeAsync(Request(), cancellation.Token));

        var cancellationException = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancellationException.CancellationToken);
    }

    [Fact]
    public async Task Handler_resolves_with_the_default_support_graph()
    {
        var services = new ServiceCollection();
        services.AddTenonAdminWorkflow();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<AiDecisionNodeHandler>();

        Assert.IsAssignableFrom<IWorkflowNodeHandler>(handler);
        Assert.Equal(WfNodeType.AiDecision, handler.NodeType);

        var result = await handler.ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        var outcome = Assert.IsType<AiDecisionOutcome>(result.AiDecision);
        Assert.Equal(AiDecisionFallbackReason.ProviderFailure, outcome.FallbackReason);
        Assert.Null(result.OutputJson);
    }

    [Fact]
    public async Task Nested_policy_configuration_binds_arrays_and_affects_the_registered_evaluator()
    {
        var configuration = ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:Policy:MinimumConfidence"] = "0.91",
            ["TenonAdmin:Workflow:AiDecision:Policy:MinimumEvidenceCount"] = "2",
            ["TenonAdmin:Workflow:AiDecision:Policy:HighRiskFlags:0"] = "financial",
            ["TenonAdmin:Workflow:AiDecision:Policy:HighRiskFlags:1"] = "legal",
            ["TenonAdmin:Workflow:AiDecision:Policy:AllowedReasonCodes:0"] = "AI_REVIEW",
            ["TenonAdmin:Workflow:AiDecision:Policy:AllowedReasonCodes:1"] = "HUMAN_REVIEW",
        });
        var services = new ServiceCollection();
        services.AddTenonAdminWorkflow(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<WorkflowOptions>();
        var parser = scope.ServiceProvider.GetRequiredService<IAiDecisionProposalParser>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAiDecisionPolicyEvaluator>();

        Assert.Equal(0.91m, options.AiDecision.Policy.MinimumConfidence);
        Assert.Equal(2, options.AiDecision.Policy.MinimumEvidenceCount);
        Assert.Equal(["financial", "legal"], options.AiDecision.Policy.HighRiskFlags);
        Assert.Equal(["AI_REVIEW", "HUMAN_REVIEW"], options.AiDecision.Policy.AllowedReasonCodes);

        Assert.Equal(
            AiDecisionPolicyClassification.LowConfidence,
            evaluator.Evaluate(ParseProposal(parser, confidence: 0.90m, evidenceCount: 2)).Classification);
        Assert.Equal(
            AiDecisionPolicyClassification.HighRisk,
            evaluator.Evaluate(ParseProposal(parser, confidence: 0.95m, evidenceCount: 2, riskFlags: ["financial"])).Classification);
        Assert.Equal(
            AiDecisionPolicyClassification.EvidenceInsufficient,
            evaluator.Evaluate(ParseProposal(parser, confidence: 0.95m, evidenceCount: 1)).Classification);
        Assert.Equal(
            AiDecisionPolicyClassification.DisallowedReason,
            evaluator.Evaluate(ParseProposal(
                parser,
                confidence: 0.95m,
                evidenceCount: 2,
                reasonCodes: ["POLICY_MATCH"])).Classification);
    }

    [Theory]
    [InlineData("MinimumConfidence", "1.01")]
    [InlineData("MinimumEvidenceCount", "0")]
    [InlineData("AllowedReasonCodes:0", "not_canonical")]
    public void Invalid_ai_policy_configuration_is_rejected_during_AddTenonAdminWorkflow(
        string policyKey,
        string value)
    {
        var services = new ServiceCollection();
        var configuration = ConfigurationFor(new Dictionary<string, string?>
        {
            [$"TenonAdmin:Workflow:AiDecision:Policy:{policyKey}"] = value,
        });

        var exception = Record.Exception(() => services.AddTenonAdminWorkflow(configuration));

        var invalidConfiguration = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("TenonAdmin:Workflow:AiDecision:Policy", invalidConfiguration.Message);
    }

    [Fact]
    public void Pre_registered_invalid_workflow_options_are_preserved_and_rejected_when_policy_evaluator_resolves()
    {
        foreach (var preRegisteredOptions in InvalidPreRegisteredWorkflowOptions())
        {
            var services = new ServiceCollection();
            services.AddSingleton(preRegisteredOptions);
            services.AddTenonAdminWorkflow();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.Same(preRegisteredOptions, scope.ServiceProvider.GetRequiredService<WorkflowOptions>());

            var exception = Record.Exception(
                () => scope.ServiceProvider.GetRequiredService<IAiDecisionPolicyEvaluator>());

            var invalidConfiguration = Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("TenonAdmin:Workflow:AiDecision", invalidConfiguration.Message);
        }
    }

    [Fact]
    public void Ai_decision_registrations_are_singleton_or_scoped_as_intended_without_duplicates()
    {
        var services = new ServiceCollection();

        services.AddTenonAdminWorkflow();
        services.AddTenonAdminWorkflow();

        AssertDescriptor(services, typeof(WorkflowOptions), ServiceLifetime.Singleton);
        AssertDescriptor(services, typeof(IAiDecisionProposalParser), ServiceLifetime.Singleton);
        AssertDescriptor(services, typeof(IAiDecisionPolicyEvaluator), ServiceLifetime.Singleton);
        AssertDescriptor(services, typeof(IAiDecisionProvider), ServiceLifetime.Scoped);
        AssertDescriptor(services, typeof(AiDecisionNodeHandler), ServiceLifetime.Scoped);
        var nodeHandlerDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IWorkflowNodeHandler))
            .ToArray();
        Assert.Equal(2, nodeHandlerDescriptors.Length);
        Assert.Contains(nodeHandlerDescriptors, descriptor =>
            descriptor.ImplementationType == typeof(AiDecisionNodeHandler));
        Assert.Contains(nodeHandlerDescriptors, descriptor =>
            descriptor.ImplementationFactory is not null);

        var jobs = new AdminJobsOptions();
        using var jobHttpClient = new JobHttpClient(jobs);
        using var nodeHandlerProvider = new ServiceCollection()
            .AddSingleton(jobHttpClient)
            .AddSingleton(jobs)
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
        var webhookDescriptor = Assert.Single(nodeHandlerDescriptors, descriptor =>
            descriptor.ImplementationFactory is not null);
        var nodeHandler = webhookDescriptor.ImplementationFactory!(nodeHandlerProvider);
        Assert.IsType<WebhookNodeHandler>(nodeHandler);
    }

    [Fact]
    public void Pre_registered_ai_handler_stays_first_for_the_same_node_type()
    {
        var services = new ServiceCollection();
        var jobs = new AdminJobsOptions();
        services.AddSingleton(jobs);
        services.AddSingleton(new JobHttpClient(jobs));
        services.AddScoped<IWorkflowNodeHandler>(_ =>
            new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.AiDecision));
        services.AddTenonAdminWorkflow();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var aiHandlers = scope.ServiceProvider.GetServices<IWorkflowNodeHandler>()
            .Where(handler => handler.NodeType == WfNodeType.AiDecision)
            .ToList();

        Assert.Equal(2, aiHandlers.Count);
        Assert.IsType<FakeNodeHandler>(aiHandlers[0]);
        Assert.IsType<AiDecisionNodeHandler>(aiHandlers[1]);
    }

    private static IEnumerable<WorkflowOptions> InvalidPreRegisteredWorkflowOptions()
    {
        yield return new WorkflowOptions { AiDecision = null! };
        yield return new WorkflowOptions
        {
            AiDecision = new AiDecisionOptions { Policy = null! },
        };
    }

    private static void AssertDescriptor(
        IServiceCollection services,
        Type serviceType,
        ServiceLifetime expectedLifetime)
    {
        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == serviceType);
        Assert.Equal(expectedLifetime, descriptor.Lifetime);
    }

    private static IConfiguration ConfigurationFor(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static AiDecisionProviderRequest Request(string executionKey = "ai-registration-execution") => new()
    {
        ExecutionKey = executionKey,
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
        ExecutionKey = "ai-registration-execution",
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

    private static AiDecisionProposal ParseProposal(
        IAiDecisionProposalParser parser,
        decimal confidence,
        int evidenceCount,
        string[]? reasonCodes = null,
        string[]? riskFlags = null)
    {
        var proposalJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            recommendation = "approve",
            confidence,
            reasonCodes = reasonCodes ?? ["AI_REVIEW"],
            rationale = "server configuration test",
            evidence = Enumerable.Range(0, evidenceCount).Select(index => new
            {
                id = $"evidence-{index}",
                source = "workflow-input",
                contentHash = EvidenceHash,
            }),
            riskFlags = riskFlags ?? [],
        });
        var parsed = parser.Parse(proposalJson);

        Assert.True(parsed.IsValid);
        return Assert.IsType<AiDecisionProposal>(parsed.Proposal);
    }
}
