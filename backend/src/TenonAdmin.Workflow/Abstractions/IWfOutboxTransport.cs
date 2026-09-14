namespace TenonAdmin.Workflow;

/// <summary>
/// outbox 投递结果类型。数值不落库,只指导消费者回写;刻意无 0 值,避免默认成功。
/// </summary>
public enum WfOutboxDispatchOutcome
{
    Succeeded = 1,
    RetryableFailure = 2,
    TerminalFailure = 3,
}

/// <summary>
/// 一次 transport 调用的结果。私有构造 + 工厂,避免成功却带错误码这类矛盾状态。
/// </summary>
public sealed class WfOutboxDispatchResult
{
    private WfOutboxDispatchResult()
    {
    }

    public required WfOutboxDispatchOutcome Type { get; init; }

    public string? Summary { get; init; }

    public int? ErrorCode { get; init; }

    /// <summary>仅 <see cref="WfOutboxDispatchOutcome.RetryableFailure"/> 有意义;<c>null</c> = 由 dispatcher 退避。</summary>
    public TimeSpan? RetryAfter { get; init; }

    public static WfOutboxDispatchResult Succeeded(string? summary = null) => new()
    {
        Type = WfOutboxDispatchOutcome.Succeeded,
        Summary = summary,
    };

    public static WfOutboxDispatchResult RetryableFailure(
        int? errorCode = null,
        string? summary = null,
        TimeSpan? retryAfter = null) => new()
    {
        Type = WfOutboxDispatchOutcome.RetryableFailure,
        ErrorCode = errorCode,
        Summary = summary,
        RetryAfter = retryAfter,
    };

    public static WfOutboxDispatchResult TerminalFailure(int? errorCode = null, string? summary = null) => new()
    {
        Type = WfOutboxDispatchOutcome.TerminalFailure,
        ErrorCode = errorCode,
        Summary = summary,
    };
}

/// <summary>交给 transport 的只读消息快照;不含数据库会话。</summary>
public sealed class WfOutboxMessage
{
    public required long Id { get; init; }

    public required long ExecutionId { get; init; }

    public required string MessageType { get; init; }

    public required string MessageKey { get; init; }

    public string? PayloadJson { get; init; }

    /// <summary>领取后读回的 <see cref="WfOutbox.AttemptCount"/>,1 基,不再 +1。</summary>
    public required int AttemptCount { get; init; }
}

/// <summary>
/// outbox 外部投递 SPI。内置默认是本地确认(无外部通道);消费者前置注册同接口即可换成 HTTP/MQ。
/// 调用发生在事务外;实现不得推进 task/token,也不得自行开工作流事务。
/// </summary>
public interface IWfOutboxTransport
{
    Task<WfOutboxDispatchResult> DispatchAsync(WfOutboxMessage message, CancellationToken cancellationToken);
}
