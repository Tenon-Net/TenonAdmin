namespace TenonAdmin.Workflow;

/// <summary>
/// 分支条件求值 SPI(仅结构化条件,非脚本)。变量源 = 实例 <c>wf_instance.VariablesJson</c>——
/// 发起时由前端原样提交、后端从不校验(<c>WfInstanceService.StartAsync</c> 直接透传),
/// 因此实现必须对烂 JSON 免疫。
/// <para>
/// 失败安全约定:求值不抛异常,任何字段缺失 / 类型不确定 / 解析失败一律按 <c>false</c> 处理,
/// 从而让分支落到设计器强制配置的默认臂,不阻断引擎事务。
/// </para>
/// 消费者可前置 <c>TryAdd</c> 注册同接口整体替换。
/// </summary>
public interface IWfConditionEvaluator
{
    /// <summary>
    /// 对 <paramref name="expr"/> 求值。<paramref name="expr"/> 为 <c>null</c> 或
    /// <paramref name="variablesJson"/> 为 null/空白/非法 JSON/根非 object 时,均视作「无匹配」返回 <c>false</c>,
    /// 不抛异常。
    /// </summary>
    bool Evaluate(WfConditionExpr? expr, string? variablesJson);
}
