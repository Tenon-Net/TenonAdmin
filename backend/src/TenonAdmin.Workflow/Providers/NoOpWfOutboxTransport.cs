namespace TenonAdmin.Workflow;

/// <summary>
/// 内置 outbox transport:内核不知道外部通道,明确返回终态失败。
/// 消费者要真正投递 HTTP/MQ 时,在 <c>AddTenonAdminWorkflow</c> 之前注册自己的
/// <see cref="IWfOutboxTransport"/>。这样忘记配置不会把消息伪装成已投递。
/// </summary>
public class NoOpWfOutboxTransport : IWfOutboxTransport
{
    public virtual Task<WfOutboxDispatchResult> DispatchAsync(
        WfOutboxMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WfOutboxDispatchResult.TerminalFailure(
            WorkflowErrorCode.OutboxTransportNotConfigured,
            "未配置真实 outbox transport"));
    }
}
