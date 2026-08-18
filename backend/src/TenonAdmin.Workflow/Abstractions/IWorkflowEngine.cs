namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流引擎入口(SPI)。一次 <see cref="ExecuteAsync"/> = 一条 Cmd = 一个 DB 事务 + Agenda 循环。
/// 消费者可前置注册同接口整体替换内置引擎。
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// 执行一条命令(启动 / 完成待办等)。事务内跑完 Agenda;成功提交后返回结果
    /// (通知等后置动作由服务层根据结果触发,不进事务)。
    /// </summary>
    Task<WfEngineResult> ExecuteAsync(IWfCommand command, CancellationToken cancellationToken = default);
}
