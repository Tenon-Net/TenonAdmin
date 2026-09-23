using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

/// <summary>
/// 仅用于费用报销评测假设的服务端规则门槛，不注册为生产默认制度。
/// 复用确定性规则但不读取样本标注；只降级候选，保留模型原始建议与人工兜底。
/// </summary>
public sealed class ExpenseAiDecisionPolicyEvaluator(AiDecisionPolicyOptions options)
    : AiDecisionPolicyEvaluator(options)
{
    public const string PolicyVersion = "expense-guard-v2";

    // 缺少结构化输入时不允许通过候选；避免旧调用入口绕过业务规则。
    public override AiDecisionPolicyEvaluation Evaluate(AiDecisionProposal proposal) =>
        ApplyRules(proposal, base.Evaluate(proposal), eligible: false);

    public override AiDecisionPolicyEvaluation Evaluate(
        AiDecisionProposal proposal,
        IReadOnlyDictionary<string, JsonElement> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var evaluation = base.Evaluate(proposal);
        var eligible = evaluation.Classification == AiDecisionPolicyClassification.ShadowCandidate
            && ExpenseEvalOracle.Decide(JsonSerializer.SerializeToElement(inputs)).Label == ExpenseLabel.Approve;
        return ApplyRules(proposal, evaluation, eligible);
    }

    private static AiDecisionPolicyEvaluation ApplyRules(
        AiDecisionProposal proposal,
        AiDecisionPolicyEvaluation evaluation,
        bool eligible) => AiDecisionPolicyEvaluation.Create(
            proposal,
            evaluation.Classification == AiDecisionPolicyClassification.ShadowCandidate && !eligible
                ? AiDecisionPolicyClassification.BusinessRuleMismatch
                : evaluation.Classification,
            PolicyVersion);
}
