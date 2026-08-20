using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="EnterNodeOp.SelectArm"/> 纯单测——不起宿主,配真实 <see cref="WfConditionEvaluator"/>。
/// 覆盖定案语义(Round 12 评审 P2①,原先零覆盖):非默认臂只要命中就赢,即便默认臂排在数组更前面
/// (钉死「默认臂短路」不能吞掉后面能命中的条件臂);默认臂只在无条件臂命中时兜底,即便自己配了
/// 一条必然为真的 <c>Expr</c> 也不参与求值。
/// </summary>
public class WfSelectArmTests
{
    private readonly WfConditionEvaluator _evaluator = new();
    private readonly Probe _probe = new();

    private static WfConditionExpr Leaf(string field, WfConditionOp op, object value) => new()
    {
        Field = field,
        Op = op,
        Value = JsonSerializer.SerializeToElement(value),
    };

    [Fact]
    public void Matching_condition_arm_wins_even_when_default_arm_is_listed_first()
    {
        var defaultArm = new WfBranchArm
        {
            Id = "default",
            Name = "默认",
            IsDefault = true,
            Expr = Leaf("nope", WfConditionOp.Empty, ""),
        };
        var conditionArm = new WfBranchArm
        {
            Id = "high",
            Name = "大额",
            IsDefault = false,
            Expr = Leaf("amount", WfConditionOp.Gt, 100),
        };
        var vars = JsonSerializer.Serialize(new Dictionary<string, object> { ["amount"] = 200 });

        var picked = _probe.Pick([defaultArm, conditionArm], _evaluator, vars);

        Assert.Same(conditionArm, picked);
    }

    [Fact]
    public void Default_arm_expr_is_never_evaluated_and_still_wins_as_fallback()
    {
        // 默认臂配了一条必然为真的 Expr(字段缺失时 empty 恒真);唯一的条件臂不命中。
        var defaultArm = new WfBranchArm
        {
            Id = "default",
            Name = "默认",
            IsDefault = true,
            Expr = Leaf("neverUsedField", WfConditionOp.Empty, ""),
        };
        var conditionArm = new WfBranchArm
        {
            Id = "high",
            Name = "大额",
            IsDefault = false,
            Expr = Leaf("amount", WfConditionOp.Gt, 100),
        };
        var vars = JsonSerializer.Serialize(new Dictionary<string, object> { ["amount"] = 10 });

        var picked = _probe.Pick([defaultArm, conditionArm], _evaluator, vars);

        Assert.Same(defaultArm, picked);
    }

    private sealed class Probe() : EnterNodeOp(new WfNode())
    {
        public WfBranchArm? Pick(IReadOnlyList<WfBranchArm> arms, IWfConditionEvaluator evaluator, string? variablesJson) =>
            SelectArm(arms, evaluator, variablesJson);
    }
}
