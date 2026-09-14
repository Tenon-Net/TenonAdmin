using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>死信 outbox 分页与人工重放;消费者可整体替换。</summary>
public interface IWfOutboxService
{
    Task<PagedList<WfOutboxOutput>> PageAsync(
        WfOutboxPageInput input,
        CancellationToken cancellationToken = default);

    Task<WfOutboxOutput> ReplayAsync(
        long id,
        string? requestId,
        CancellationToken cancellationToken = default);
}

/// <summary>outbox 监控分页。缺省只看 <see cref="WfOutboxStatus.Failed"/> 死信。</summary>
public record WfOutboxPageInput : PageInputBase
{
    public WfOutboxStatus? Status { get; init; }

    public string? MessageType { get; init; }
}

public record WfOutboxOutput
{
    public long Id { get; init; }

    public long ExecutionId { get; init; }

    public long InstanceId { get; init; }

    public string MessageType { get; init; } = "";

    public string MessageKey { get; init; } = "";

    public string? PayloadJson { get; init; }

    public WfOutboxStatus Status { get; init; }

    public int AttemptCount { get; init; }

    public DateTime AvailableAtUtc { get; init; }

    public string? LastError { get; init; }

    public DateTime? CompletedAtUtc { get; init; }
}
