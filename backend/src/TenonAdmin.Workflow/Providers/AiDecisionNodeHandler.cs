namespace TenonAdmin.Workflow;

/// <summary>
/// AI Decision 的 shadow-only 执行器。它经既有 dispatcher 在事务外调用；
/// 只完成 Provider → schema → policy 的类型化交接，任何完成路径都明确转人工。
/// </summary>
public sealed class AiDecisionNodeHandler : IWorkflowNodeHandler
{
    private const string ProposalSummary = "AI 决策建议已转人工处理。";
    private const string MalformedProposalSummary = "AI 决策提案格式无效，已转人工处理。";
    private const string ProviderTimeoutSummary = "AI 决策服务超时，已转人工处理。";
    private const string ProviderFailureSummary = "AI 决策服务不可用，已转人工处理。";

    private readonly IAiDecisionProvider _provider;
    private readonly IAiDecisionProposalParser _parser;
    private readonly IAiDecisionPolicyEvaluator _policy;

    public WfNodeType NodeType => WfNodeType.AiDecision;

    public AiDecisionNodeHandler(
        IAiDecisionProvider provider,
        IAiDecisionProposalParser parser,
        IAiDecisionPolicyEvaluator policy)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(policy);

        _provider = provider;
        _parser = parser;
        _policy = policy;
    }

    /// <summary>
    /// 调用 Provider 前先验证 context 并响应外部取消。安全输入投影拒绝、Provider 内部超时或取消、
    /// 以及 Provider 的其他非取消异常都收敛成安全的人工兜底；外部取消原样传播，不输出错误正文。
    /// </summary>
    public async Task<WfNodeExecutionResult> ExecuteAsync(
        WfNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        ValidateContext(context);
        cancellationToken.ThrowIfCancellationRequested();

        AiDecisionProviderRequest request;
        try
        {
            request = CreateProviderRequest(context);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            return CreateManualFallback(
                AiDecisionFallbackReason.ProviderFailure,
                ProviderFailureSummary,
                AiDecisionProviderResultType.Failed,
                inputHash: null);
        }

        AiDecisionProviderResult? providerResult;
        try
        {
            providerResult = await _provider.ProposeAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateManualFallback(
                AiDecisionFallbackReason.ProviderTimeout,
                ProviderTimeoutSummary,
                AiDecisionProviderResultType.TimedOut,
                request.InputHash);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateManualFallback(
                AiDecisionFallbackReason.ProviderFailure,
                ProviderFailureSummary,
                AiDecisionProviderResultType.Failed,
                request.InputHash);
        }

        // parser / policy 是进程内契约。其未分类异常交给 dispatcher 统一记录，不能伪装成 Provider 故障。
        return HandleProviderResult(providerResult, request.InputHash);
    }

    private WfNodeExecutionResult HandleProviderResult(AiDecisionProviderResult? providerResult, string? inputHash)
    {
        if (providerResult is null)
        {
            return CreateManualFallback(
                AiDecisionFallbackReason.ProviderFailure,
                ProviderFailureSummary,
                AiDecisionProviderResultType.Failed,
                inputHash);
        }

        return providerResult.Type switch
        {
            AiDecisionProviderResultType.Proposal when providerResult.ProposalJson is not null =>
                HandleProposal(providerResult, inputHash),
            AiDecisionProviderResultType.TimedOut =>
                CreateManualFallback(
                    AiDecisionFallbackReason.ProviderTimeout,
                    ProviderTimeoutSummary,
                    AiDecisionProviderResultType.TimedOut,
                    inputHash,
                    providerResult),
            _ => CreateManualFallback(
                AiDecisionFallbackReason.ProviderFailure,
                ProviderFailureSummary,
                AiDecisionProviderResultType.Failed,
                inputHash,
                providerResult),
        };
    }

    private WfNodeExecutionResult HandleProposal(AiDecisionProviderResult providerResult, string? inputHash)
    {
        var parsed = _parser.Parse(providerResult.ProposalJson);
        if (!parsed.IsValid || parsed.Proposal is null)
        {
            return CreateManualFallback(
                AiDecisionFallbackReason.MalformedProposal,
                MalformedProposalSummary,
                AiDecisionProviderResultType.Proposal,
                inputHash,
                providerResult);
        }

        var evaluation = _policy.Evaluate(parsed.Proposal);
        var fallbackReason = evaluation.Classification switch
        {
            AiDecisionPolicyClassification.ShadowCandidate => AiDecisionFallbackReason.ShadowOnly,
            AiDecisionPolicyClassification.RejectRecommended => AiDecisionFallbackReason.RejectNotAllowed,
            AiDecisionPolicyClassification.ManualRequested => AiDecisionFallbackReason.ManualRequested,
            AiDecisionPolicyClassification.LowConfidence => AiDecisionFallbackReason.LowConfidence,
            AiDecisionPolicyClassification.HighRisk => AiDecisionFallbackReason.HighRisk,
            AiDecisionPolicyClassification.EvidenceInsufficient => AiDecisionFallbackReason.EvidenceInsufficient,
            AiDecisionPolicyClassification.DisallowedReason => AiDecisionFallbackReason.DisallowedReason,
            _ => AiDecisionFallbackReason.ProviderFailure,
        };

        return WfNodeExecutionResult.AiManualFallback(
            AiDecisionOutcome.ManualFallback(
                parsed.Proposal,
                evaluation,
                fallbackReason,
                AiDecisionProviderResultType.Proposal,
                inputHash,
                providerResult: providerResult),
            summary: ProposalSummary);
    }

    private static WfNodeExecutionResult CreateManualFallback(
        AiDecisionFallbackReason fallbackReason,
        string summary,
        AiDecisionProviderResultType providerResultType,
        string? inputHash,
        AiDecisionProviderResult? providerResult = null) =>
        WfNodeExecutionResult.AiManualFallback(
            AiDecisionOutcome.ManualFallback(
                fallbackReason,
                providerResultType,
                inputHash,
                providerResult),
            summary: summary);

    private static AiDecisionProviderRequest CreateProviderRequest(WfNodeExecutionContext context)
    {
        var projection = AiDecisionSafeInputProjector.Create(context.NodeProps, context.VariablesJson);
        return new AiDecisionProviderRequest
        {
            ExecutionKey = context.ExecutionKey,
            InstanceId = context.InstanceId,
            TokenId = context.TokenId,
            NodeVisitId = context.NodeVisitId,
            NodeId = context.NodeId,
            DefinitionVersionId = context.DefinitionVersionId,
            OrgId = context.OrgId,
            StarterUserId = context.StarterUserId,
            BusinessKey = context.BusinessKey,
            Attempt = context.Attempt,
            DeadlineAtUtc = context.DeadlineAtUtc,
            Instructions = projection.Instructions,
            Inputs = projection.Inputs,
            CanonicalInputsJson = projection.CanonicalInputsJson,
            InputHash = projection.InputHash,
        };
    }

    private static void ValidateContext(WfNodeExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ExecutionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.NodeId);

        if (context.InstanceId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context.InstanceId));
        }

        if (context.TokenId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context.TokenId));
        }

        if (context.DefinitionVersionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context.DefinitionVersionId));
        }

        if (context.StarterUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context.StarterUserId));
        }

        if (context.Attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(context.Attempt));
        }

        if (context.DeadlineAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("DeadlineAtUtc 必须是 UTC。", nameof(context.DeadlineAtUtc));
        }
    }
}
