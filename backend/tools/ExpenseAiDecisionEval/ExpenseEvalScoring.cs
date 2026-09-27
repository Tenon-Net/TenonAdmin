using TenonAdmin.Workflow;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public sealed record EvalObservation
{
    public required string CaseId { get; init; }

    public required string Split { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public required IReadOnlyList<string> RuleIds { get; init; }

    public required string Rationale { get; init; }

    public required ExpenseLabel Expected { get; init; }

    public ExpenseLabel? RawRecommendation { get; init; }

    public AiDecisionPolicyClassification? PolicyClassification { get; init; }

    public string? TechnicalFailure { get; init; }

    public string? FallbackReason { get; init; }

    public string? ExceptionType { get; init; }

    public decimal? UncalibratedReportedConfidence { get; init; }

    public long? PromptTokens { get; init; }

    public long? CompletionTokens { get; init; }

    public long? TotalTokens { get; init; }

    public required IReadOnlyList<long> AttemptLatenciesMs { get; init; }

    public required int Attempts { get; init; }

    public required int TimeoutAttempts { get; init; }

    public required int ProviderFailureAttempts { get; init; }

    public required string HandlerResultType { get; init; }
}

public readonly record struct ExpenseEvalRatio(int Count, int Denominator);

public sealed class ExpenseEvalMetrics
{
    public required string Source { get; init; }

    public required int Total { get; init; }

    public required IReadOnlyDictionary<string, int> ByExpected { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> RawConfusion { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> PolicyConfusion { get; init; }

    public required int RawMatched { get; init; }

    public required int PolicyMatched { get; init; }

    /// <summary>期望为拒绝或转人工、却给出通过建议的数量。分母含技术失败，不剔除。</summary>
    public required ExpenseEvalRatio FalseApproveRaw { get; init; }

    public required ExpenseEvalRatio FalseApprovePolicy { get; init; }

    public required IReadOnlyList<string> FalseApproveRawCaseIds { get; init; }

    public required IReadOnlyList<string> FalseApprovePolicyCaseIds { get; init; }

    public required ExpenseEvalRatio FalseRejectRaw { get; init; }

    public required ExpenseEvalRatio FalseRejectPolicy { get; init; }

    public required ExpenseEvalRatio ManualRaw { get; init; }

    public required ExpenseEvalRatio ManualPolicy { get; init; }

    public required int ParseFailureCases { get; init; }

    public required int TimeoutCases { get; init; }

    public required int ProviderFailureCases { get; init; }

    public required int TimeoutAttempts { get; init; }

    public required int ProviderFailureAttempts { get; init; }

    public required IReadOnlyList<string> RetriedAfterTechnicalFailureCaseIds { get; init; }

    public required int LatencySamples { get; init; }

    public long? LatencyP50Ms { get; init; }

    public long? LatencyP95Ms { get; init; }

    public required int CasesWithUsage { get; init; }

    public required int CasesMissingUsage { get; init; }

    public long? PromptTokens { get; init; }

    public long? CompletionTokens { get; init; }

    public long? TotalTokens { get; init; }

    public required IReadOnlyList<ExpenseEvalFailure> Failures { get; init; }

    public string DisagreementName { get; init; } = "与标注不一致";

    public bool ReportedConfidenceIsCalibrated { get; init; }
}

public sealed record ExpenseEvalFailure(
    string CaseId,
    string Split,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> RuleIds,
    string Expected,
    string? RawSuggestion,
    string? PolicySuggestion,
    string? PolicyClassification,
    string? TechnicalFailure,
    string? FallbackReason,
    int Attempts,
    string Rationale);

public static class ExpenseEvalScoring
{
    public static readonly string[] MatrixColumns = ["approve", "reject", "manual", "technical_failure"];

    public static readonly string[] MatrixRows = ["approve", "reject", "manual"];

    public static IReadOnlyList<EvalObservation> Fixture { get; } = CreateFixture();

    public static ExpenseLabel MapPolicy(AiDecisionPolicyClassification classification) => classification switch
    {
        AiDecisionPolicyClassification.ShadowCandidate => ExpenseLabel.Approve,
        AiDecisionPolicyClassification.RejectRecommended => ExpenseLabel.Reject,
        AiDecisionPolicyClassification.ManualRequested
            or AiDecisionPolicyClassification.LowConfidence
            or AiDecisionPolicyClassification.HighRisk
            or AiDecisionPolicyClassification.EvidenceInsufficient
            or AiDecisionPolicyClassification.DisallowedReason
            or AiDecisionPolicyClassification.BusinessRuleMismatch => ExpenseLabel.Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(classification)),
    };

    public static long? PercentileNearestRank(IReadOnlyList<long> sortedAscending, int numerator, int denominator)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);
        if (sortedAscending.Count == 0)
        {
            return null;
        }

        if (numerator <= 0 || denominator <= 0 || numerator > denominator)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator));
        }

        var rank = (numerator * sortedAscending.Count + denominator - 1) / denominator;
        return sortedAscending[rank - 1];
    }

    public static ExpenseEvalMetrics Summarize(string source, IReadOnlyList<EvalObservation> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var raw = EmptyMatrix();
        var policy = EmptyMatrix();
        var byExpected = MatrixRows.ToDictionary(label => label, _ => 0, StringComparer.Ordinal);
        List<string> falseApproveRaw = [];
        List<string> falseApprovePolicy = [];
        var falseApproveDenominator = 0;
        var falseRejectRaw = 0;
        var falseRejectPolicy = 0;
        var falseRejectDenominator = 0;
        var manualRaw = 0;
        var manualPolicy = 0;
        var rawMatched = 0;
        var policyMatched = 0;
        var parseFailures = 0;
        var timeoutCases = 0;
        var providerFailureCases = 0;
        var timeoutAttempts = 0;
        var providerFailureAttempts = 0;
        List<string> retried = [];
        List<long> latencies = [];
        long prompt = 0;
        long completion = 0;
        long totalTokens = 0;
        var withUsage = 0;
        var missingUsage = 0;
        List<ExpenseEvalFailure> failures = [];

        foreach (var row in rows.OrderBy(item => item.CaseId, StringComparer.Ordinal))
        {
            if (row.TechnicalFailure is null)
            {
                if (row.RawRecommendation is null || row.PolicyClassification is null)
                {
                    throw new ExpenseEvalException($"{row.CaseId} 的有效建议缺少 policy 分类。");
                }
            }
            else if (row.RawRecommendation is not null || row.PolicyClassification is not null)
            {
                throw new ExpenseEvalException($"{row.CaseId} 的技术失败不能同时保留建议。");
            }

            var expected = ExpenseEvalOracle.ToWire(row.Expected);
            byExpected[expected]++;
            var rawLabel = Predict(row, policyLayer: false);
            var policyLabel = Predict(row, policyLayer: true);
            raw[expected][rawLabel]++;
            policy[expected][policyLabel]++;
            if (rawLabel == expected)
            {
                rawMatched++;
            }

            if (policyLabel == expected)
            {
                policyMatched++;
            }

            if (row.Expected is ExpenseLabel.Reject or ExpenseLabel.Manual)
            {
                falseApproveDenominator++;
                if (rawLabel == "approve")
                {
                    falseApproveRaw.Add(row.CaseId);
                }

                if (policyLabel == "approve")
                {
                    falseApprovePolicy.Add(row.CaseId);
                }
            }

            if (row.Expected is ExpenseLabel.Approve or ExpenseLabel.Manual)
            {
                falseRejectDenominator++;
                if (rawLabel == "reject")
                {
                    falseRejectRaw++;
                }

                if (policyLabel == "reject")
                {
                    falseRejectPolicy++;
                }
            }

            if (rawLabel == "manual")
            {
                manualRaw++;
            }

            if (policyLabel == "manual")
            {
                manualPolicy++;
            }

            switch (row.TechnicalFailure)
            {
                case "parse_failure":
                    parseFailures++;
                    break;
                case "timeout":
                    timeoutCases++;
                    break;
                case "provider_failure":
                    providerFailureCases++;
                    break;
                case null:
                    break;
                default:
                    throw new ExpenseEvalException("未知的技术失败类别。");
            }

            timeoutAttempts += row.TimeoutAttempts;
            providerFailureAttempts += row.ProviderFailureAttempts;
            if (row.TechnicalFailure is null && (row.TimeoutAttempts > 0 || row.ProviderFailureAttempts > 0))
            {
                retried.Add(row.CaseId);
            }

            latencies.AddRange(row.AttemptLatenciesMs);
            if (row.PromptTokens is long promptTokens
                && row.CompletionTokens is long completionTokens
                && row.TotalTokens is long rowTotal)
            {
                withUsage++;
                prompt += promptTokens;
                completion += completionTokens;
                totalTokens += rowTotal;
            }
            else
            {
                missingUsage++;
            }

            if (rawLabel != expected || policyLabel != expected)
            {
                failures.Add(new ExpenseEvalFailure(
                    row.CaseId,
                    row.Split,
                    row.Tags,
                    row.RuleIds,
                    expected,
                    row.RawRecommendation is null ? null : ExpenseEvalOracle.ToWire(row.RawRecommendation.Value),
                    row.PolicyClassification is null ? null : ExpenseEvalOracle.ToWire(MapPolicy(row.PolicyClassification.Value)),
                    row.PolicyClassification?.ToString(),
                    row.TechnicalFailure,
                    row.FallbackReason,
                    row.Attempts,
                    row.Rationale));
            }
        }

        latencies.Sort();
        return new ExpenseEvalMetrics
        {
            Source = source,
            Total = rows.Count,
            ByExpected = byExpected,
            RawConfusion = raw.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, int>)pair.Value,
                StringComparer.Ordinal),
            PolicyConfusion = policy.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, int>)pair.Value,
                StringComparer.Ordinal),
            RawMatched = rawMatched,
            PolicyMatched = policyMatched,
            FalseApproveRaw = new ExpenseEvalRatio(falseApproveRaw.Count, falseApproveDenominator),
            FalseApprovePolicy = new ExpenseEvalRatio(falseApprovePolicy.Count, falseApproveDenominator),
            FalseApproveRawCaseIds = falseApproveRaw,
            FalseApprovePolicyCaseIds = falseApprovePolicy,
            FalseRejectRaw = new ExpenseEvalRatio(falseRejectRaw, falseRejectDenominator),
            FalseRejectPolicy = new ExpenseEvalRatio(falseRejectPolicy, falseRejectDenominator),
            ManualRaw = new ExpenseEvalRatio(manualRaw, rows.Count),
            ManualPolicy = new ExpenseEvalRatio(manualPolicy, rows.Count),
            ParseFailureCases = parseFailures,
            TimeoutCases = timeoutCases,
            ProviderFailureCases = providerFailureCases,
            TimeoutAttempts = timeoutAttempts,
            ProviderFailureAttempts = providerFailureAttempts,
            RetriedAfterTechnicalFailureCaseIds = retried,
            LatencySamples = latencies.Count,
            LatencyP50Ms = PercentileNearestRank(latencies, 50, 100),
            LatencyP95Ms = PercentileNearestRank(latencies, 95, 100),
            CasesWithUsage = withUsage,
            CasesMissingUsage = missingUsage,
            PromptTokens = withUsage == 0 ? null : prompt,
            CompletionTokens = withUsage == 0 ? null : completion,
            TotalTokens = withUsage == 0 ? null : totalTokens,
            Failures = failures,
        };
    }

    private static string Predict(EvalObservation row, bool policyLayer)
    {
        if (row.TechnicalFailure is not null)
        {
            return "technical_failure";
        }

        if (policyLayer)
        {
            return ExpenseEvalOracle.ToWire(MapPolicy(row.PolicyClassification!.Value));
        }

        return ExpenseEvalOracle.ToWire(row.RawRecommendation!.Value);
    }

    private static Dictionary<string, Dictionary<string, int>> EmptyMatrix()
    {
        var matrix = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var row in MatrixRows)
        {
            matrix[row] = MatrixColumns.ToDictionary(column => column, _ => 0, StringComparer.Ordinal);
        }

        return matrix;
    }

    private static IReadOnlyList<EvalObservation> CreateFixture()
    {
        const string rationale = "计分夹具，不是模型输出。";
        return
        [
            Row("F-01", ExpenseLabel.Approve, ExpenseLabel.Approve, AiDecisionPolicyClassification.ShadowCandidate, null, "ShadowOnly", 10, (10, 5, 15), ["R7", "R9"]),
            Row("F-02", ExpenseLabel.Reject, ExpenseLabel.Approve, AiDecisionPolicyClassification.ShadowCandidate, null, "ShadowOnly", 20, (10, 5, 15), ["R4", "R9"]),
            Row("F-03", ExpenseLabel.Manual, ExpenseLabel.Approve, AiDecisionPolicyClassification.LowConfidence, null, "LowConfidence", 30, null, ["R8", "R9"]),
            Row("F-04", ExpenseLabel.Approve, ExpenseLabel.Reject, AiDecisionPolicyClassification.RejectRecommended, null, "RejectNotAllowed", 40, (8, 4, 12), ["R7", "R9"]),
            Row("F-05", ExpenseLabel.Reject, null, null, "timeout", "ProviderTimeout", 50, null, ["R4", "R9"], timeoutAttempts: 1),
            Row("F-06", ExpenseLabel.Manual, null, null, "parse_failure", "MalformedProposal", 60, null, ["R8", "R9"]),
            Row("F-07", ExpenseLabel.Approve, null, null, "provider_failure", "ProviderFailure", 70, null, ["R7", "R9"], providerFailureAttempts: 1),
            Row("F-08", ExpenseLabel.Manual, ExpenseLabel.Manual, AiDecisionPolicyClassification.ManualRequested, null, "ManualRequested", 80, null, ["R8", "R9"]),
            Row("F-09", ExpenseLabel.Reject, ExpenseLabel.Reject, AiDecisionPolicyClassification.RejectRecommended, null, "RejectNotAllowed", 90, null, ["R5", "R9"]),
            Row("F-10", ExpenseLabel.Approve, ExpenseLabel.Approve, AiDecisionPolicyClassification.DisallowedReason, null, "DisallowedReason", 100, null, ["R7", "R9"]),
        ];

        static EvalObservation Row(
            string caseId,
            ExpenseLabel expected,
            ExpenseLabel? raw,
            AiDecisionPolicyClassification? classification,
            string? technical,
            string fallback,
            long latency,
            (long Prompt, long Completion, long Total)? tokens,
            IReadOnlyList<string> rules,
            int timeoutAttempts = 0,
            int providerFailureAttempts = 0) => new()
        {
            CaseId = caseId,
            Split = "fixture",
            Tags = ["scoring-fixture"],
            RuleIds = rules,
            Rationale = rationale,
            Expected = expected,
            RawRecommendation = raw,
            PolicyClassification = classification,
            TechnicalFailure = technical,
            FallbackReason = fallback,
            AttemptLatenciesMs = [latency],
            Attempts = 1,
            TimeoutAttempts = timeoutAttempts,
            ProviderFailureAttempts = providerFailureAttempts,
            PromptTokens = tokens?.Prompt,
            CompletionTokens = tokens?.Completion,
            TotalTokens = tokens?.Total,
            HandlerResultType = "ManualFallback",
        };
    }
}
