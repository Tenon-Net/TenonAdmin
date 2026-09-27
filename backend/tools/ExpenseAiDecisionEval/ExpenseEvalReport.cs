using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public sealed class ExpenseEvalReport
{
    public string ReportVersion { get; init; } = "expense-ai-decision-eval-v0";

    public required string Mode { get; init; }

    public required bool ModelEvaluated { get; init; }

    public required bool LiveExecuted { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LiveSkippedReason { get; init; }

    public required string GeneratedAtUtc { get; init; }

    public string Determinism { get; init; } = "自检可重复。真实模型调用不保证完全确定。";

    public string ClaimLimit { get; init; } =
        "合成样本只用于发现问题，不能作为生产自动审批上线证明。模型自报 confidence 不是经过校准的正确概率。不一致只称与标注不一致。";

    public required CodeIdentity Code { get; init; }

    public required DatasetIdentity Dataset { get; init; }

    public required PromptIdentity Prompt { get; init; }

    public required PolicyIdentity Policy { get; init; }

    public required RunLimits Limits { get; init; }

    public required ExpenseEvalSmoke PipelineSmoke { get; init; }

    public required ExpenseEvalMetrics ScoringSelfTest { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProviderEvaluation? ProviderEvaluation { get; init; }

    public string CoverageBoundary { get; init; } = ExpenseEvalScenario.CoverageBoundary;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ExpenseEvalReport Create(
        ExpenseEvalDataset dataset,
        AiDecisionPolicyOptions policy,
        ExpenseEvalSmoke smoke,
        bool liveExecuted,
        string? liveSkippedReason,
        string? split,
        string? model,
        int? timeoutSeconds,
        IReadOnlyList<EvalObservation>? providerObservations,
        ExpenseEvalMetrics? providerMetrics,
        bool httpApiKeyOptIn = false,
        bool expensePolicyEnabled = false)
    {
        var mode = liveExecuted ? "live" : "self-check";
        return new ExpenseEvalReport
        {
            Mode = mode,
            ModelEvaluated = liveExecuted,
            LiveExecuted = liveExecuted,
            LiveSkippedReason = liveExecuted ? null : liveSkippedReason,
            GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Code = ReadCode(ExpenseEvalPaths.RepositoryRoot()),
            Dataset = DescribeDataset(dataset),
            Prompt = new PromptIdentity
            {
                ProviderPromptVersion = ExpenseEvalScenario.ProviderPromptVersion,
                SystemInstructionSha256 = ExpenseEvalScenario.SystemInstructionSha256,
                NodeInstructionsId = ExpenseEvalScenario.NodeInstructionsId,
                NodeInstructionsSha256 = ExpenseEvalScenario.NodeInstructionsSha256,
            },
            Policy = DescribePolicy(policy, expensePolicyEnabled),
            Limits = new RunLimits
            {
                MaxAttemptsPerCase = ExpenseEvalLimits.MaxAttemptsPerCase,
                AbsoluteMaxProviderCalls = ExpenseEvalLimits.AbsoluteMaxProviderCalls,
                TimeoutSeconds = timeoutSeconds,
                PlannedProviderCalls = split is null ? 0 : ExpenseEvalLimits.CallBudget(dataset.Select(split).Count),
                Split = split,
                RetryOn = ["timeout", "provider_failure"],
                NoRetryOn = ["parse_failure"],
                HttpApiKeyOptIn = httpApiKeyOptIn,
            },
            PipelineSmoke = smoke,
            ScoringSelfTest = ExpenseEvalScoring.Summarize("fixed-fixture", ExpenseEvalScoring.Fixture),
            ProviderEvaluation = providerMetrics is null
                ? null
                : new ProviderEvaluation
                {
                    Split = split ?? "holdout",
                    Model = model,
                    IndependentClaimAllowed = false,
                    ManualFallbackCases = providerObservations?.Count(item =>
                        item.HandlerResultType == nameof(WfNodeExecutionResultType.ManualFallback)) ?? 0,
                    WorkflowAdvancedCases = providerObservations?.Count(item =>
                        item.HandlerResultType == nameof(WfNodeExecutionResultType.Succeeded)) ?? 0,
                    Metrics = providerMetrics,
                },
        };
    }

    public void Write(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine);
    }

    private static DatasetIdentity DescribeDataset(ExpenseEvalDataset dataset)
    {
        var byExpected = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var byTag = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in dataset.Cases)
        {
            var label = ExpenseEvalOracle.ToWire(item.Expected);
            byExpected[label] = byExpected.GetValueOrDefault(label) + 1;
            foreach (var tag in item.Tags)
            {
                byTag[tag] = byTag.GetValueOrDefault(tag) + 1;
            }
        }

        return new DatasetIdentity
        {
            DatasetId = ExpenseEvalScenario.DatasetId,
            RulesVersion = ExpenseEvalOracle.RulesVersion,
            Sha256 = ExpenseEvalScenario.Sha256File(dataset.Path),
            Cases = dataset.Cases.Count,
            Dev = dataset.Cases.Count(item => item.Split == "dev"),
            Holdout = dataset.Cases.Count(item => item.Split == "holdout"),
            ByExpected = byExpected,
            ByTag = byTag,
            SyntheticOnly = true,
            TagCountsOverlap = true,
        };
    }

    private static PolicyIdentity DescribePolicy(AiDecisionPolicyOptions policy, bool expensePolicyEnabled)
    {
        var canonical = ExpenseEvalScenario.CanonicalPolicy(policy);
        var factory = ExpenseEvalScenario.CanonicalPolicy(new AiDecisionPolicyOptions());
        var implementationSha256 = expensePolicyEnabled ? ExpensePolicyImplementationIdentity.Sha256 : null;
        return new PolicyIdentity
        {
            Version = expensePolicyEnabled ? ExpenseAiDecisionPolicyEvaluator.PolicyVersion : policy.Version,
            BasePolicyVersion = expensePolicyEnabled ? policy.Version : null,
            Sha256 = ExpenseEvalScenario.Sha256(expensePolicyEnabled
                ? canonical + "\n" + ExpenseAiDecisionPolicyEvaluator.PolicyVersion + "\n"
                    + ExpenseEvalOracle.RulesVersion + "\n" + implementationSha256
                : canonical),
            ImplementationSha256 = implementationSha256,
            MinimumConfidence = policy.MinimumConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MinimumEvidenceCount = policy.MinimumEvidenceCount,
            AllowedReasonCodes = policy.AllowedReasonCodes.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            HighRiskFlags = policy.HighRiskFlags.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            MatchesFactoryDefault = !expensePolicyEnabled && string.Equals(canonical, factory, StringComparison.Ordinal),
            EncodesExpenseRules = expensePolicyEnabled,
        };
    }

    private static CodeIdentity ReadCode(string root)
    {
        try
        {
            var commit = Git(root, "rev-parse", "HEAD");
            var status = Git(root, "status", "--porcelain");
            return new CodeIdentity { GitCommit = commit.Trim(), Dirty = status.Length > 0 };
        }
        catch (Exception)
        {
            return new CodeIdentity { GitCommit = "unknown", Dirty = null };
        }
    }

    private static string Git(string root, params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            throw new ExpenseEvalException("git 不可用。");
        }

        _ = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new ExpenseEvalException("git 不可用。");
        }

        return stdout.GetAwaiter().GetResult();
    }
}

public sealed class CodeIdentity
{
    public required string GitCommit { get; init; }

    public bool? Dirty { get; init; }
}

public sealed class DatasetIdentity
{
    public required string DatasetId { get; init; }

    public required string RulesVersion { get; init; }

    public required string Sha256 { get; init; }

    public required int Cases { get; init; }

    public required int Dev { get; init; }

    public required int Holdout { get; init; }

    public required IReadOnlyDictionary<string, int> ByExpected { get; init; }

    public required IReadOnlyDictionary<string, int> ByTag { get; init; }

    public required bool SyntheticOnly { get; init; }

    public required bool TagCountsOverlap { get; init; }
}

public sealed class PromptIdentity
{
    public required string ProviderPromptVersion { get; init; }

    public required string SystemInstructionSha256 { get; init; }

    public required string NodeInstructionsId { get; init; }

    public required string NodeInstructionsSha256 { get; init; }
}

public sealed class PolicyIdentity
{
    public required string Version { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BasePolicyVersion { get; init; }

    public required string Sha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImplementationSha256 { get; init; }

    public required string MinimumConfidence { get; init; }

    public required int MinimumEvidenceCount { get; init; }

    public required IReadOnlyList<string> AllowedReasonCodes { get; init; }

    public required IReadOnlyList<string> HighRiskFlags { get; init; }

    public required bool MatchesFactoryDefault { get; init; }

    public required bool EncodesExpenseRules { get; init; }
}

public sealed class RunLimits
{
    public required int MaxAttemptsPerCase { get; init; }

    public required int AbsoluteMaxProviderCalls { get; init; }

    public int? TimeoutSeconds { get; init; }

    public required int PlannedProviderCalls { get; init; }

    public string? Split { get; init; }

    public required IReadOnlyList<string> RetryOn { get; init; }

    public required IReadOnlyList<string> NoRetryOn { get; init; }

    /// <summary>评测显式允许在 HTTP 上附加本地密钥。生产 Provider 不接受这个组合。</summary>
    public bool HttpApiKeyOptIn { get; init; }
}

public sealed class ProviderEvaluation
{
    public required string Split { get; init; }

    public string? Model { get; init; }

    public required bool IndependentClaimAllowed { get; init; }

    public required int ManualFallbackCases { get; init; }

    public required int WorkflowAdvancedCases { get; init; }

    public required ExpenseEvalMetrics Metrics { get; init; }
}
