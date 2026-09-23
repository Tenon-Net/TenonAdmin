using System.Diagnostics;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public sealed class ExpenseEvalSmoke
{
    public required int Cases { get; init; }

    public required int ProviderCalls { get; init; }

    public required int ManualFallback { get; init; }

    public int Succeeded { get; init; }

    public int RetryableFailure { get; init; }

    public int TerminalFailure { get; init; }

    public bool WorkflowAdvanced { get; init; }
}

/// <summary>
/// 直接调用 AI Decision handler。安全投影在 handler 内部完成。
/// 这里不注册调度器，也不写审计或推进 task/token。
/// </summary>
public static class ExpenseEvalHarness
{
    public static async Task<ExpenseEvalSmoke> RunSmokeAsync(
        IReadOnlyList<ExpenseEvalCase> cases,
        CancellationToken cancellationToken = default,
        bool expensePolicyEnabled = false)
    {
        var provider = new ScriptedApproveProvider();
        AiDecisionPolicyEvaluator policy = expensePolicyEnabled
            ? new ExpenseAiDecisionPolicyEvaluator(new AiDecisionPolicyOptions())
            : new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions());
        var manual = 0;
        foreach (var item in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = await ExecuteCaseAsync(item, provider, policy, timeoutSeconds: 30, cancellationToken);
            if (observation.HandlerResultType != nameof(WfNodeExecutionResultType.ManualFallback)
                || (observation.FallbackReason != nameof(AiDecisionFallbackReason.ShadowOnly)
                    && !(expensePolicyEnabled && observation.FallbackReason == nameof(AiDecisionFallbackReason.BusinessRuleMismatch)))
                || observation.RawRecommendation != ExpenseLabel.Approve)
            {
                throw new ExpenseEvalException($"{item.CaseId} 没有走通 shadow-only 投影接线。");
            }

            manual++;
        }

        if (provider.Calls != cases.Count || manual != cases.Count)
        {
            throw new ExpenseEvalException("自检接线没有覆盖全部案例。");
        }

        return new ExpenseEvalSmoke
        {
            Cases = cases.Count,
            ProviderCalls = provider.Calls,
            ManualFallback = manual,
        };
    }

    public static async Task<IReadOnlyList<EvalObservation>> RunProviderAsync(
        IReadOnlyList<ExpenseEvalCase> cases,
        IAiDecisionProvider provider,
        AiDecisionPolicyEvaluator policy,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ExpenseEvalLimits.EnsureCallBudget(cases.Count);
        if (timeoutSeconds is < OpenAiCompatibleAiDecisionOptions.MinimumTimeoutSeconds
            or > OpenAiCompatibleAiDecisionOptions.MaximumTimeoutSeconds)
        {
            throw new ExpenseEvalException("超时必须沿用 Provider 允许的 1 到 120 秒。");
        }

        List<EvalObservation> observations = [];
        foreach (var item in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observations.Add(await ExecuteCaseAsync(item, provider, policy, timeoutSeconds, cancellationToken));
        }

        return observations;
    }

    public static async Task<EvalObservation> ExecuteCaseAsync(
        ExpenseEvalCase item,
        IAiDecisionProvider provider,
        AiDecisionPolicyEvaluator policy,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var handler = new AiDecisionNodeHandler(provider, new AiDecisionProposalParser(), policy);
        List<long> latencies = [];
        var timeoutAttempts = 0;
        var providerFailureAttempts = 0;
        AttemptCapture? final = null;
        for (var attempt = 1; attempt <= ExpenseEvalLimits.MaxAttemptsPerCase; attempt++)
        {
            var capture = await CaptureAttemptAsync(handler, item, attempt, timeoutSeconds, cancellationToken);
            latencies.Add(capture.LatencyMs);
            if (capture.TechnicalFailure == "timeout")
            {
                timeoutAttempts++;
            }
            else if (capture.TechnicalFailure == "provider_failure")
            {
                providerFailureAttempts++;
            }

            final = capture;
            var retry = capture.TechnicalFailure is "timeout" or "provider_failure"
                && attempt < ExpenseEvalLimits.MaxAttemptsPerCase;
            if (!retry)
            {
                break;
            }
        }

        var chosen = final ?? throw new ExpenseEvalException($"{item.CaseId} 没有产生评测结果。");
        return new EvalObservation
        {
            CaseId = item.CaseId,
            Split = item.Split,
            Tags = item.Tags,
            RuleIds = item.RuleIds,
            Rationale = item.Rationale,
            Expected = item.Expected,
            RawRecommendation = chosen.RawRecommendation,
            PolicyClassification = chosen.PolicyClassification,
            TechnicalFailure = chosen.TechnicalFailure,
            FallbackReason = chosen.FallbackReason,
            ExceptionType = chosen.ExceptionType,
            UncalibratedReportedConfidence = chosen.Confidence,
            PromptTokens = chosen.PromptTokens,
            CompletionTokens = chosen.CompletionTokens,
            TotalTokens = chosen.TotalTokens,
            AttemptLatenciesMs = latencies,
            Attempts = latencies.Count,
            TimeoutAttempts = timeoutAttempts,
            ProviderFailureAttempts = providerFailureAttempts,
            HandlerResultType = chosen.HandlerResultType,
        };
    }

    private static async Task<AttemptCapture> CaptureAttemptAsync(
        AiDecisionNodeHandler handler,
        ExpenseEvalCase item,
        int attempt,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var backstop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            backstop.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds + 5));
            var result = await handler.ExecuteAsync(CreateContext(item, attempt, timeoutSeconds), backstop.Token);
            watch.Stop();
            if (result.Type != WfNodeExecutionResultType.ManualFallback)
            {
                throw new ExpenseEvalException($"{item.CaseId} 返回了人工兜底以外的执行结果。");
            }

            return CaptureResult(result, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            watch.Stop();
            return AttemptCapture.Technical("timeout", nameof(AiDecisionFallbackReason.ProviderTimeout), watch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ExpenseEvalException)
        {
            watch.Stop();
            return AttemptCapture.Technical(
                "provider_failure",
                nameof(AiDecisionFallbackReason.ProviderFailure),
                watch.ElapsedMilliseconds,
                exception.GetType().Name);
        }
    }

    private static AttemptCapture CaptureResult(WfNodeExecutionResult result, long latencyMs)
    {
        var outcome = result.AiDecision;
        if (outcome is null)
        {
            return AttemptCapture.Technical(
                "provider_failure",
                nameof(AiDecisionFallbackReason.ProviderFailure),
                latencyMs);
        }

        var technical = outcome.FallbackReason switch
        {
            AiDecisionFallbackReason.MalformedProposal => "parse_failure",
            AiDecisionFallbackReason.ProviderTimeout => "timeout",
            AiDecisionFallbackReason.ProviderFailure => "provider_failure",
            _ => null,
        };
        if (technical is not null)
        {
            return AttemptCapture.Technical(technical, outcome.FallbackReason.ToString(), latencyMs) with
            {
                PromptTokens = outcome.PromptTokens,
                CompletionTokens = outcome.CompletionTokens,
                TotalTokens = outcome.TotalTokens,
            };
        }

        if (outcome.Recommendation is null || outcome.PolicyClassification is null)
        {
            throw new ExpenseEvalException("有效 proposal 缺少建议或 policy 分类。");
        }

        return new AttemptCapture
        {
            LatencyMs = latencyMs,
            HandlerResultType = nameof(WfNodeExecutionResultType.ManualFallback),
            RawRecommendation = outcome.Recommendation.Value switch
            {
                AiDecisionRecommendation.Approve => ExpenseLabel.Approve,
                AiDecisionRecommendation.Reject => ExpenseLabel.Reject,
                AiDecisionRecommendation.Manual => ExpenseLabel.Manual,
                _ => throw new ExpenseEvalException("未知的 proposal 建议。"),
            },
            PolicyClassification = outcome.PolicyClassification,
            TechnicalFailure = null,
            FallbackReason = outcome.FallbackReason.ToString(),
            Confidence = outcome.Proposal?.Confidence,
            PromptTokens = outcome.PromptTokens,
            CompletionTokens = outcome.CompletionTokens,
            TotalTokens = outcome.TotalTokens,
        };
    }

    private static WfNodeExecutionContext CreateContext(ExpenseEvalCase item, int attempt, int timeoutSeconds) => new()
    {
        ExecutionKey = "expense-eval:" + item.CaseId + ":attempt:" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
        InstanceId = 910001,
        TokenId = 910001,
        NodeVisitId = 910001,
        NodeId = "expense-eval",
        NodeType = WfNodeType.AiDecision,
        DefinitionVersionId = 910001,
        OrgId = 910001,
        StarterUserId = 910001,
        NodeProps = new WfNodeProps
        {
            AiInstructions = ExpenseEvalScenario.NodeInstructions,
            AiInputFields = [.. ExpenseEvalScenario.InputFields],
        },
        VariablesJson = item.InputJson,
        Attempt = attempt,
        DeadlineAtUtc = TimeProvider.System.GetUtcNow().AddSeconds(timeoutSeconds),
    };

    private sealed record AttemptCapture
    {
        public required long LatencyMs { get; init; }

        public required string HandlerResultType { get; init; }

        public ExpenseLabel? RawRecommendation { get; init; }

        public AiDecisionPolicyClassification? PolicyClassification { get; init; }

        public string? TechnicalFailure { get; init; }

        public string? FallbackReason { get; init; }

        public string? ExceptionType { get; init; }

        public decimal? Confidence { get; init; }

        public long? PromptTokens { get; init; }

        public long? CompletionTokens { get; init; }

        public long? TotalTokens { get; init; }

        public static AttemptCapture Technical(string technical, string fallback, long latencyMs, string? exceptionType = null) => new()
        {
            LatencyMs = latencyMs,
            HandlerResultType = exceptionType is null ? nameof(WfNodeExecutionResultType.ManualFallback) : "exception",
            TechnicalFailure = technical,
            FallbackReason = fallback,
            ExceptionType = exceptionType,
        };
    }

    private sealed class ScriptedApproveProvider : IAiDecisionProvider
    {
        public int Calls { get; private set; }

        public Task<AiDecisionProviderResult> ProposeAsync(
            AiDecisionProviderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(AiDecisionProviderResult.Proposal(
                ExpenseEvalScenario.ProposalJson("approve", "scripted-shadow-proposal"),
                provider: "scripted",
                model: "none",
                promptVersion: "v0"));
        }
    }
}

public static class ExpenseEvalPaths
{
    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "backend", "TenonAdmin.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new ExpenseEvalException("找不到仓库根目录。");
    }

    public static string DatasetPath() => Path.Combine(
        RepositoryRoot(),
        "backend",
        "tools",
        "ExpenseAiDecisionEval",
        "dataset",
        "expense-reimbursement-v0.jsonl");

    public static string RulesDocPath() => Path.Combine(
        RepositoryRoot(),
        "docs",
        "workflow",
        "expense-ai-decision-eval-v0.md");

    public static string DefaultReportPath() => Path.Combine(
        RepositoryRoot(),
        "backend",
        "tools",
        "ExpenseAiDecisionEval",
        "artifacts",
        "expense-eval-report.json");
}
