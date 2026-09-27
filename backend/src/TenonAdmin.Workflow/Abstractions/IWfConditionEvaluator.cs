namespace TenonAdmin.Workflow;

/// <summary>
/// 分支条件求值 SPI(仅结构化条件,非脚本)。变量源 = 实例 <c>wf_instance.VariablesJson</c>。
/// 变量入口会拒绝非法 JSON、重复键和非 object 根节点。
/// <para>
/// 失败安全约定:字段缺失或类型不确定按 <c>false</c> 处理；变量格式错误抛业务错误，不能掩盖持久化损坏。
/// </para>
/// 消费者可前置 <c>TryAdd</c> 注册同接口整体替换。
/// </summary>
public interface IWfConditionEvaluator
{
    /// <summary>
    /// 对 <paramref name="expr"/> 求值。<paramref name="expr"/> 为 <c>null</c> 或
    /// <paramref name="variablesJson"/> 为 null/空白时视作无字段；非法 JSON、重复键或根非 object 时抛业务错误。
    /// </summary>
    bool Evaluate(WfConditionExpr? expr, string? variablesJson);
}
