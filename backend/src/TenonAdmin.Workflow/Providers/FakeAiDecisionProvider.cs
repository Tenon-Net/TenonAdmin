using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>
/// 确定性 Fake Provider 的情景；只追加不重排，0 保持非法，避免 <c>default</c> 静默成为成功。
/// </summary>
public enum FakeAiDecisionScenario
{
    Success = 1,
    MalformedProposal = 2,
    LowConfidence = 3,
    HighRisk = 4,
    TimedOut = 5,
    Failed = 6,
    Throws = 7,
}

/// <summary>
/// 供消费者和生产测试前置注入的确定性 AI Decision Provider。它不保留请求、不访问网络或数据库，
/// 也不替换工作流默认的 fail-closed Provider。
/// </summary>
public class FakeAiDecisionProvider : IAiDecisionProvider
{
    private const string ProviderName = "fake";
    private const string ModelName = "deterministic-fake";
    private const string PromptVersion = "v0";
    /// <summary>HighRisk 情景唯一输出的精确风险标识；测试 policy 必须显式允许此值。</summary>
    public const string HighRiskFlag = "fake-high-risk";

    private const string EvidenceHash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Rationale = "Deterministic fake proposal matches the configured policy.";
    private const string ThrowsMessage = "The deterministic fake AI decision provider failed.";

    private readonly FakeAiDecisionScenario _scenario;
    private int _callCount;

    /// <summary>构造默认成功情景。</summary>
    public FakeAiDecisionProvider()
        : this(FakeAiDecisionScenario.Success)
    {
    }

    /// <summary>构造指定的确定性情景。</summary>
    public FakeAiDecisionProvider(FakeAiDecisionScenario scenario)
    {
        ValidateScenario(scenario);
        _scenario = scenario;
    }

    /// <summary>已完成且通过请求校验的调用次数。</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <inheritdoc />
    public virtual Task<AiDecisionProviderResult> ProposeAsync(
        AiDecisionProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiDecisionProviderRequestValidator.Validate(request);
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(CreateResult());
    }

    /// <summary>按固定情景生成受控 Provider 结果；派生测试适配器可仅替换这一步。</summary>
    protected virtual AiDecisionProviderResult CreateResult() => _scenario switch
    {
        FakeAiDecisionScenario.Success => AiDecisionProviderResult.Proposal(
            CreateProposalJson(0.95m, []), ProviderName, ModelName, PromptVersion),
        FakeAiDecisionScenario.MalformedProposal => AiDecisionProviderResult.Proposal(
            CreateMalformedProposalJson(), ProviderName, ModelName, PromptVersion),
        FakeAiDecisionScenario.LowConfidence => AiDecisionProviderResult.Proposal(
            CreateProposalJson(0.79m, []), ProviderName, ModelName, PromptVersion),
        FakeAiDecisionScenario.HighRisk => AiDecisionProviderResult.Proposal(
            CreateProposalJson(0.95m, [HighRiskFlag]), ProviderName, ModelName, PromptVersion),
        FakeAiDecisionScenario.TimedOut => AiDecisionProviderResult.TimedOut(ProviderName, ModelName, PromptVersion),
        FakeAiDecisionScenario.Failed => AiDecisionProviderResult.Failed(null, ProviderName, ModelName, PromptVersion),
        FakeAiDecisionScenario.Throws => throw new InvalidOperationException(ThrowsMessage),
        _ => throw new InvalidOperationException("Unsupported fake AI decision scenario."),
    };

    /// <summary>以固定属性顺序序列化合规 proposal，避免时钟、随机数或请求数据参与输出。</summary>
    protected virtual string CreateProposalJson(decimal confidence, string[] riskFlags) => JsonSerializer.Serialize(new
    {
        schemaVersion = AiDecisionProposalParser.SupportedSchemaVersion,
        recommendation = "approve",
        confidence,
        reasonCodes = new[] { "POLICY_MATCH" },
        rationale = Rationale,
        evidence = new[]
        {
            new
            {
                id = "fake-evidence",
                source = "fake-provider",
                contentHash = EvidenceHash,
            },
        },
        riskFlags,
    });

    /// <summary>生成结构上不完整但 JSON 语法有效的 proposal，供 parser fail-closed 路径使用。</summary>
    protected virtual string CreateMalformedProposalJson() => JsonSerializer.Serialize(new
    {
        schemaVersion = AiDecisionProposalParser.SupportedSchemaVersion,
    });

    private static void ValidateScenario(FakeAiDecisionScenario scenario)
    {
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Fake AI decision scenario is not supported.");
        }
    }
}
