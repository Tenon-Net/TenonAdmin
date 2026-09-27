namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public static class ExpenseEvalProgram
{
    public static Task<int> Main(string[] args) => ExpenseEvalCli.ExecuteAsync(args);
}
