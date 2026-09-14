using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 工作流卫星包可替换性「十六件套」——锁 <see cref="WorkflowSetup.AddTenonAdminWorkflow"/> 里
/// 十六个 <c>TryAdd</c> SPI 面:<see cref="IApproverResolver"/> /
/// <see cref="IWorkflowFormBinder"/> / <see cref="IWorkflowEngine"/> /
/// <see cref="IWfConditionEvaluator"/> /
/// <see cref="IWfDefinitionService"/> / <see cref="IWfTaskService"/> /
/// <see cref="IWfInstanceService"/> / <see cref="IWfAiDecisionAuditReader"/> / <see cref="IWorkflowNotifier"/> /
/// <see cref="IWfCcService"/> / <see cref="IWfOperationReceiptService"/> /
/// / <see cref="IWfDelegationService"/> / <see cref="IWfTaskSignService"/>
/// <see cref="IAiDecisionProvider"/> / <see cref="IAiDecisionProposalParser"/> /
/// <see cref="IAiDecisionPolicyEvaluator"/>。
/// <para>
/// 真判据是<strong>前置</strong>注册即胜出(裸容器,不走 <c>ConfigureTestServices</c>+Replace):
/// 把任一 <c>TryAdd</c> 退化成 <c>Add</c> → 内置后注册覆盖 → 对应本条红。
/// </para>
/// </summary>
public class WorkflowReplaceabilityTests
{
    private const string ConsumerParserInput = "consumer-parser-proposal";
    private const string ConsumerEvidenceHash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>变异:WorkflowSetup 里 IApproverResolver 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredApproverResolver_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IApproverResolver, FakeApproverResolver>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeApproverResolver>(scope.ServiceProvider.GetRequiredService<IApproverResolver>());
    }

    /// <summary>变异:WorkflowSetup 里 IWorkflowFormBinder 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredFormBinder_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWorkflowFormBinder, FakeFormBinder>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeFormBinder>(scope.ServiceProvider.GetRequiredService<IWorkflowFormBinder>());
    }

    /// <summary>变异:WorkflowSetup 里 IWorkflowEngine 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredEngine_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWorkflowEngine, FakeEngine>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeEngine>(scope.ServiceProvider.GetRequiredService<IWorkflowEngine>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfConditionEvaluator 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredConditionEvaluator_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfConditionEvaluator, FakeConditionEvaluator>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeConditionEvaluator>(
            scope.ServiceProvider.GetRequiredService<IWfConditionEvaluator>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfDefinitionService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredDefinitionService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfDefinitionService, FakeDefinitionService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeDefinitionService>(scope.ServiceProvider.GetRequiredService<IWfDefinitionService>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfTaskService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredTaskService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfTaskService, FakeTaskService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeTaskService>(scope.ServiceProvider.GetRequiredService<IWfTaskService>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfDelegationService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredDelegationService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfDelegationService, FakeDelegationService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeDelegationService>(scope.ServiceProvider.GetRequiredService<IWfDelegationService>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfTaskSignService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredTaskSignService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfTaskSignService, FakeTaskSignService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeTaskSignService>(scope.ServiceProvider.GetRequiredService<IWfTaskSignService>());
    }

    [Theory]
    [InlineData(false, WfTaskAction.Approve)]
    [InlineData(true, WfTaskAction.Reject)]
    public async Task BuiltInTaskService_ShouldPassVariablesToCompleteTaskCmd(
        bool reject,
        WfTaskAction expectedAction)
    {
        var engine = new FakeEngine();
        IWfFormTaskService service = new WfTaskService(
            engine,
            null!, null!, null!, null!, null!, null!, null!, null!,
            NullLogger<WfTaskService>.Instance);

        const string variablesJson = "{\"amount\":42}";
        if (reject)
            await service.RejectWithVariablesAsync(1, 2, variablesJson: variablesJson);
        else
            await service.ApproveWithVariablesAsync(1, 2, variablesJson: variablesJson);

        var command = Assert.IsType<CompleteTaskCmd>(engine.LastCommand);
        Assert.Equal(expectedAction, command.Action);
        Assert.Equal(variablesJson, command.VariablesJson);
    }

    /// <summary>变异:WorkflowSetup 里 IWfInstanceService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredInstanceService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfInstanceService, FakeInstanceService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeInstanceService>(scope.ServiceProvider.GetRequiredService<IWfInstanceService>());
    }

    [Fact]
    public void WfInstanceService_preserves_the_pre_ai_audit_constructor()
    {
        Assert.NotNull(typeof(WfInstanceService).GetConstructor([
            typeof(IRepository<WfInstance>),
            typeof(IRepository<WfDefinition>),
            typeof(IRepository<WfDefinitionVersion>),
            typeof(IRepository<WfHistory>),
            typeof(IRepository<WfHisTask>),
            typeof(IRepository<WfTask>),
            typeof(IRepository<WfTaskActor>),
            typeof(IRepository<WfCc>),
            typeof(IRepository<SysUserRole>),
            typeof(IWorkflowEngine),
            typeof(ICurrentUser),
            typeof(IPermissionProvider),
        ]));
    }

    /// <summary>变异:WorkflowSetup 里 IWfAiDecisionAuditReader 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredAiDecisionAuditReader_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfAiDecisionAuditReader, FakeAiDecisionAuditReader>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeAiDecisionAuditReader>(
            scope.ServiceProvider.GetRequiredService<IWfAiDecisionAuditReader>());
    }

    /// <summary>变异:WorkflowSetup 里 IWorkflowNotifier 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredWorkflowNotifier_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWorkflowNotifier, FakeWorkflowNotifier>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeWorkflowNotifier>(scope.ServiceProvider.GetRequiredService<IWorkflowNotifier>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfCcService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredCcService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IWfCcService, FakeCcService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeCcService>(scope.ServiceProvider.GetRequiredService<IWfCcService>());
    }

    /// <summary>变异:WorkflowSetup 里 IWfOperationReceiptService 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredOperationReceiptService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s =>
            s.AddScoped<IWfOperationReceiptService, FakeOperationReceiptService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeOperationReceiptService>(
            scope.ServiceProvider.GetRequiredService<IWfOperationReceiptService>());
    }

    /// <summary>变异:WorkflowSetup 里 IAiDecisionProvider 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredAiDecisionProvider_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IAiDecisionProvider, FakeAiDecisionProvider>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeAiDecisionProvider>(scope.ServiceProvider.GetRequiredService<IAiDecisionProvider>());
    }

    /// <summary>变异:WorkflowSetup 里 IAiDecisionProposalParser 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredAiDecisionProposalParser_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s =>
            s.AddSingleton<IAiDecisionProposalParser, FakeAiDecisionProposalParser>());
        await using var scope = sp.CreateAsyncScope();
        var parser = scope.ServiceProvider.GetRequiredService<IAiDecisionProposalParser>();

        Assert.IsType<FakeAiDecisionProposalParser>(parser);
        var parsed = parser.Parse(ConsumerParserInput);
        var proposal = Assert.IsType<AiDecisionProposal>(parsed.Proposal);
        Assert.True(parsed.IsValid);
        Assert.Equal(["CONSUMER_MATCH"], proposal.ReasonCodes);
    }

    /// <summary>变异:WorkflowSetup 里 IAiDecisionPolicyEvaluator 的 TryAdd 改 Add → 本条红。</summary>
    [Fact]
    public async Task PreRegisteredAiDecisionPolicyEvaluator_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s =>
            s.AddSingleton<IAiDecisionPolicyEvaluator, FakeAiDecisionPolicyEvaluator>());
        await using var scope = sp.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAiDecisionPolicyEvaluator>();

        Assert.IsType<FakeAiDecisionPolicyEvaluator>(evaluator);
        var evaluation = evaluator.Evaluate(CreateConsumerProposal());
        Assert.Equal(AiDecisionRecommendation.Approve, evaluation.Recommendation);
        Assert.Equal(AiDecisionPolicyClassification.HighRisk, evaluation.Classification);
    }

    /// <summary>
    /// 消费者前置注册 → <c>AddTenonAdminWorkflow</c> → 解析断言。
    /// 不启宿主、不挂 SqlSugar:只验 DI 描述符,不解析内置实现(它们依赖仓储)。
    /// </summary>
    private static ServiceProvider BuildProvider(Action<IServiceCollection> preRegister)
    {
        var services = new ServiceCollection();
        preRegister(services);
        services.AddTenonAdminWorkflow();
        return services.BuildServiceProvider();
    }

    private static AiDecisionProposal CreateConsumerProposal() => AiDecisionProposal.Create(
        AiDecisionProposalParser.SupportedSchemaVersion,
        AiDecisionRecommendation.Approve,
        confidence: 0.95m,
        reasonCodes: ["CONSUMER_MATCH"],
        rationale: "consumer supplied proposal",
        evidence: [AiDecisionEvidence.Create("consumer-evidence", "consumer-source", ConsumerEvidenceHash)],
        riskFlags: []);

    private sealed class FakeAiDecisionProposalParser : IAiDecisionProposalParser
    {
        public AiDecisionProposalParseResult Parse(string? json) =>
            string.Equals(json, ConsumerParserInput, StringComparison.Ordinal)
                ? AiDecisionProposalParseResult.Valid(CreateConsumerProposal())
                : AiDecisionProposalParseResult.Invalid();
    }

    private sealed class FakeAiDecisionPolicyEvaluator : IAiDecisionPolicyEvaluator
    {
        public AiDecisionPolicyEvaluation Evaluate(AiDecisionProposal proposal)
        {
            ArgumentNullException.ThrowIfNull(proposal);
            return AiDecisionPolicyEvaluation.Create(
                proposal,
                AiDecisionPolicyClassification.HighRisk);
        }
    }

    private sealed class FakeOperationReceiptService : IWfOperationReceiptService
    {
        public Task<WfOperationReceipt?> TryBeginAsync(
            WfOperationIdentity identity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<WfOperationReceipt?>(null);

        public Task CommitAsync(
            WfOperationIdentity identity,
            int resultCode,
            string? resultJson,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeApproverResolver : IApproverResolver
    {
        public Task<IReadOnlyList<long>> ResolveAsync(
            string providerKey, ApproverResolveContext context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeFormBinder : IWorkflowFormBinder
    {
        public Task ValidateOnStartAsync(WfFormBindContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task OnInstanceCompletedAsync(WfFormBindContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeEngine : IWorkflowEngine
    {
        public IWfCommand? LastCommand { get; private set; }

        public Task<WfEngineResult> ExecuteAsync(IWfCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(new WfEngineResult
            {
                InstanceId = 1,
                InstanceStatus = WfInstanceStatus.Running,
            });
        }
    }

    private sealed class FakeConditionEvaluator : IWfConditionEvaluator
    {
        public bool Evaluate(WfConditionExpr? expr, string? variablesJson) => false;
    }

    private sealed class FakeDefinitionService : IWfDefinitionService
    {
        public Task<PagedList<WfDefinition>> PageAsync(WfDefinitionPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfDefinitionDetailOutput> GetAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<long> AddAsync(WfDefinitionInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task UpdateAsync(WfDefinitionInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<int> PublishAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DisableAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WfDefinitionVersionOutput>> ListVersionsAsync(
            long definitionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeTaskService : IWfTaskService
    {
        public Task<PagedList<WfTodoItemOutput>> PageTodoAsync(
            long userId, WfTaskPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<PagedList<WfDoneItemOutput>> PageDoneAsync(
            long userId, WfTaskPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> ApproveAsync(
            long taskId, long userId, string? comment = null, string? requestId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> RejectAsync(
            long taskId, long userId, string? comment = null, string? requestId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> TransferAsync(
            long taskId, long userId, long toUserId, string? comment = null, string? requestId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> DelegateAsync(
            long taskId, long userId, long toUserId, string? comment = null, string? requestId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task UrgeAsync(
            long taskId, long callerUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> ReturnAsync(
            long taskId, long userId, string? targetNodeId, string? comment = null, string? requestId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeInstanceService : IWfInstanceService
    {
        public Task<IReadOnlyList<WfStartableDefinitionOutput>> ListStartableAsync(
            long userId, long? orgId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfStartableDefinitionDetailOutput> GetStartableAsync(
            long definitionId, long userId, long? orgId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> StartAsync(
            WfStartInput input, long starterUserId, long? starterOrgId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<PagedList<WfInstanceListItemOutput>> PageMineAsync(
            long starterUserId, WfInstancePageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<PagedList<WfInstanceListItemOutput>> PageMonitorAsync(
            WfInstanceMonitorPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfInstanceDetailOutput> GetAsync(
            long instanceId, long currentUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WfHistoryItemOutput>> ListHistoryAsync(
            long instanceId, long currentUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> CancelAsync(
            long instanceId, long callerUserId, string? requestId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> ResubmitAsync(
            long instanceId, long callerUserId, string? variablesJson,
            IReadOnlyDictionary<string, List<long>>? selectedUserIdsByNode, string? requestId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDelegationService : IWfDelegationService
    {
        public Task<PagedList<WfDelegationRuleOutput>> PageAsync(
            WfDelegationRulePageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfDelegationRuleOutput> AddAsync(
            WfDelegationRuleInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfDelegationRuleOutput> UpdateAsync(
            long id, WfDelegationRuleInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(long id, string? requestId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WfDelegationAssignment>> ResolveAsync(
            IReadOnlyList<long> originalUserIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeTaskSignService : IWfTaskSignService
    {
        public Task<WfEngineResult> AddSignAsync(
            long taskId, long userId, long targetUserId, string? comment = null,
            string? requestId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> RemoveSignAsync(
            long taskId, long userId, long targetUserId, string? comment = null,
            string? requestId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WfEngineResult> TakeBackAsync(
            long taskId, long userId, string? comment = null,
            string? requestId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeAiDecisionAuditReader : IWfAiDecisionAuditReader
    {
        public Task<IReadOnlyList<WfAiDecisionAuditOutput>> ListAiDecisionsAsync(
            long instanceId,
            long currentUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WfAiDecisionAuditOutput>>([]);
    }

    private sealed class FakeCcService : IWfCcService
    {
        public Task<PagedList<WfCcItemOutput>> PageMineAsync(
            long userId, WfCcPageInput input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task MarkReadAsync(long ccId, long userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeWorkflowNotifier : IWorkflowNotifier
    {
        public Task TaskAssignedAsync(
            WfNotifyContext ctx, IReadOnlyList<long> userIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task InstanceCompletedAsync(WfNotifyContext ctx, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task TaskUrgedAsync(
            WfNotifyContext ctx, long taskId, long? fromUserId, IReadOnlyList<long> toUserIds,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
