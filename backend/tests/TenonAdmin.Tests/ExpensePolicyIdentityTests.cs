using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TenonAdmin.Tools.ExpenseAiDecisionEval;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

public class ExpensePolicyIdentityTests
{
    private static readonly IReadOnlyDictionary<string, string> Sources =
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["ExpensePolicyImplementation.AiDecisionContracts.cs"] = "backend/src/TenonAdmin.Workflow/Abstractions/AiDecisionContracts.cs",
            ["ExpensePolicyImplementation.AiDecisionNodeHandler.cs"] = "backend/src/TenonAdmin.Workflow/Providers/AiDecisionNodeHandler.cs",
            ["ExpensePolicyImplementation.AiDecisionPolicyEvaluator.cs"] = "backend/src/TenonAdmin.Workflow/Providers/AiDecisionPolicyEvaluator.cs",
            ["ExpensePolicyImplementation.AiDecisionProposalParser.cs"] = "backend/src/TenonAdmin.Workflow/Providers/AiDecisionProposalParser.cs",
            ["ExpensePolicyImplementation.AiDecisionSafeInputProjector.cs"] = "backend/src/TenonAdmin.Workflow/Providers/AiDecisionSafeInputProjector.cs",
            ["ExpensePolicyImplementation.ExpenseAiDecisionPolicyEvaluator.cs"] = "backend/tools/ExpenseAiDecisionEval/ExpenseAiDecisionPolicyEvaluator.cs",
            ["ExpensePolicyImplementation.ExpenseEvalOracle.cs"] = "backend/tools/ExpenseAiDecisionEval/ExpenseEvalOracle.cs",
            ["ExpensePolicyImplementation.ExpenseEvalScenario.cs"] = "backend/tools/ExpenseAiDecisionEval/ExpenseEvalScenario.cs",
        };

    [Fact]
    public void Implementation_hash_covers_the_exact_compiled_policy_sources()
    {
        var assembly = typeof(ExpensePolicyImplementationIdentity).Assembly;
        var actualNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("ExpensePolicyImplementation.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(Sources.Keys, actualNames);

        var manifest = new StringBuilder();
        foreach (var (resourceName, relativePath) in Sources)
        {
            using var embedded = assembly.GetManifestResourceStream(resourceName);
            Assert.NotNull(embedded);
            var embeddedHash = Convert.ToHexString(SHA256.HashData(embedded)).ToLowerInvariant();
            using var source = File.OpenRead(Path.Combine(ExpenseEvalPaths.RepositoryRoot(), relativePath));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), embeddedHash);
            manifest.Append(resourceName).Append('=').Append(embeddedHash).Append('\n');
        }

        Assert.Equal(ExpenseEvalScenario.Sha256(manifest.ToString()), ExpensePolicyImplementationIdentity.Sha256);
    }

    [Fact]
    public void Report_exposes_implementation_hash_only_when_expense_policy_is_enabled()
    {
        var options = new AiDecisionPolicyOptions();
        var disabled = CreateReport(expensePolicyEnabled: false).Policy;
        var enabled = CreateReport(expensePolicyEnabled: true).Policy;

        Assert.Null(disabled.ImplementationSha256);
        Assert.DoesNotContain("implementationSha256", JsonSerializer.Serialize(disabled, ExpenseEvalReport.JsonOptions));
        Assert.Equal(ExpenseEvalScenario.Sha256(ExpenseEvalScenario.CanonicalPolicy(options)), disabled.Sha256);
        Assert.Equal(ExpensePolicyImplementationIdentity.Sha256, enabled.ImplementationSha256);
        Assert.Equal(
            ExpenseEvalScenario.Sha256(
                ExpenseEvalScenario.CanonicalPolicy(options) + "\n"
                + ExpenseAiDecisionPolicyEvaluator.PolicyVersion + "\n"
                + ExpenseEvalOracle.RulesVersion + "\n"
                + ExpensePolicyImplementationIdentity.Sha256),
            enabled.Sha256);
    }

    private static ExpenseEvalReport CreateReport(bool expensePolicyEnabled) => ExpenseEvalReport.Create(
        ExpenseEvalDataset.LoadOfficial(ExpenseEvalPaths.DatasetPath()),
        new AiDecisionPolicyOptions(),
        new ExpenseEvalSmoke { Cases = 0, ProviderCalls = 0, ManualFallback = 0 },
        liveExecuted: false,
        liveSkippedReason: "test",
        split: null,
        model: null,
        timeoutSeconds: null,
        providerObservations: null,
        providerMetrics: null,
        expensePolicyEnabled: expensePolicyEnabled);
}
