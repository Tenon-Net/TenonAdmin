namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流通知 SPI(待办到达 / 完结等)。默认实现可对接内核通知;
/// 消费者前置注册即整体替换。
/// </summary>
public interface IWorkflowNotifier
{
}
