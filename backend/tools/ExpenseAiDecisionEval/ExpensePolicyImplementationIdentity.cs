using System.Security.Cryptography;
using System.Text;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public static class ExpensePolicyImplementationIdentity
{
    private static readonly string[] ResourceNames =
    [
        "ExpensePolicyImplementation.AiDecisionContracts.cs",
        "ExpensePolicyImplementation.AiDecisionNodeHandler.cs",
        "ExpensePolicyImplementation.AiDecisionPolicyEvaluator.cs",
        "ExpensePolicyImplementation.AiDecisionProposalParser.cs",
        "ExpensePolicyImplementation.AiDecisionSafeInputProjector.cs",
        "ExpensePolicyImplementation.ExpenseAiDecisionPolicyEvaluator.cs",
        "ExpensePolicyImplementation.ExpenseEvalOracle.cs",
        "ExpensePolicyImplementation.ExpenseEvalScenario.cs",
    ];

    public static string Sha256 { get; } = ComputeSha256();

    private static string ComputeSha256()
    {
        var assembly = typeof(ExpensePolicyImplementationIdentity).Assembly;
        var manifest = new StringBuilder();
        foreach (var resourceName in ResourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new ExpenseEvalException($"缺少 policy 实现资源：{resourceName}。");
            manifest.Append(resourceName)
                .Append('=')
                .Append(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant())
                .Append('\n');
        }

        return ExpenseEvalScenario.Sha256(manifest.ToString());
    }
}
