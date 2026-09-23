namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

/// <summary>离线评测的固定预算。真实 Provider 调用不能从命令行把这些上限调高。</summary>
public static class ExpenseEvalLimits
{
    public const int MaxAttemptsPerCase = 2;

    public const int AbsoluteMaxProviderCalls = 128;

    public const int OfficialCaseCount = 60;

    public const int OfficialDevCount = 8;

    public const int OfficialHoldoutCount = 52;

    public static int CallBudget(int caseCount) => checked(caseCount * MaxAttemptsPerCase);

    public static void EnsureCallBudget(int caseCount)
    {
        if (caseCount < 0 || CallBudget(caseCount) > AbsoluteMaxProviderCalls)
        {
            throw new ExpenseEvalException(
                $"真实调用预算超限：{caseCount} 条 × {MaxAttemptsPerCase} 次，上限 {AbsoluteMaxProviderCalls} 次。");
        }
    }
}

public sealed class ExpenseEvalException : Exception
{
    public ExpenseEvalException(string message)
        : base(message)
    {
    }
}
