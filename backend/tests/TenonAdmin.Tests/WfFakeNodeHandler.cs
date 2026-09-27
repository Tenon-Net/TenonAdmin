using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 测试用 <see cref="IWorkflowNodeHandler"/> 假实现——按构造参数原样返回结果。
/// <b>绝不进生产 DI</b>,只放测试程序集:内核包里躺一个「可配置返回任意结果」的 handler,
/// 消费者误注册会在生产里静默短路掉某个节点类型。
/// </summary>
internal sealed class FakeNodeHandler(
    WfNodeExecutionResult result,
    WfNodeType nodeType = WfNodeType.Webhook) : IWorkflowNodeHandler
{
    public WfNodeType NodeType => nodeType;

    /// <summary>调用计数,供 Task 6/7 断言「同一 ExecutionKey 只调一次」。</summary>
    public int CallCount { get; private set; }

    /// <summary>最后一次收到的上下文,供 Task 6 断言快照投影正确。</summary>
    public WfNodeExecutionContext? LastContext { get; private set; }

    /// <summary>
    /// 可选钩子,在 <see cref="CallCount"/> 递增之后、返回结果之前调用——供 Task 6 在 handler 内部断言
    /// 「此刻没有活动数据库事务」(<c>db.Ado.IsAnyTran()</c>)。
    /// </summary>
    public Action? OnExecute { get; init; }

    public Task<WfNodeExecutionResult> ExecuteAsync(WfNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); // 取消走异常,不走返回值
        CallCount++;
        LastContext = context;
        OnExecute?.Invoke();
        return Task.FromResult(result);
    }
}
