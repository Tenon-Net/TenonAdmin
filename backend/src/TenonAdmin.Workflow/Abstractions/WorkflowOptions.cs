namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流全局配置(对应 <c>TenonAdmin:Workflow</c>)。
/// 节点 / 流程级配置可覆盖此处默认值(空审批人策略等,见设计方案)。
/// </summary>
public sealed class WorkflowOptions
{
    /// <summary>
    /// 空审批人全局默认策略:<c>autoPass</c>(自动通过,出厂默认) /
    /// <c>transfer</c>(转指定人) / <c>block</c>(卡住并通知管理员)。
    /// </summary>
    public string Nobody { get; set; } = "autoPass";
}
