using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>一条待写入的出站调用记录(不含正文与请求头)。</summary>
public sealed record OutboundLogEntry
{
    public string Target { get; init; } = "";
    public string? Operation { get; init; }
    public string HttpMethod { get; init; } = "";
    public string Url { get; init; } = "";
    public int? StatusCode { get; init; }
    public OutboundOutcome Outcome { get; init; }
    public string? Reason { get; init; }
    public string? ErrorSummary { get; init; }
    public long ElapsedMs { get; init; }
    public string CallId { get; init; } = "";
    public string? TraceId { get; init; }
    public long? DeliveryId { get; init; }
    public int? AttemptNo { get; init; }
}

/// <summary>出站调用记录分页查询入参(全部可选;时间按记录时间,本地时间口径与内核日志一致)。</summary>
public record OutboundLogPageInput : PageInputBase
{
    /// <summary>目标名(精确)。</summary>
    public string? Target { get; init; }

    /// <summary>业务操作名关键字(模糊)。</summary>
    public string? Operation { get; init; }

    public OutboundOutcome? Outcome { get; init; }

    /// <summary>关联投递记录 Id。</summary>
    public long? DeliveryId { get; init; }

    /// <summary>调用 Id(精确,即发给对方的 <c>X-Request-Id</c>)。</summary>
    public string? CallId { get; init; }

    /// <summary>追踪标识(精确)。</summary>
    public string? TraceId { get; init; }

    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
}

/// <summary>出站调用记录视图。</summary>
public record OutboundLogView
{
    public long Id { get; init; }
    public string Target { get; init; } = "";
    public string? Operation { get; init; }
    public string HttpMethod { get; init; } = "";
    public string Url { get; init; } = "";
    public int? StatusCode { get; init; }
    public OutboundOutcome Outcome { get; init; }
    public string? Reason { get; init; }
    public string? ErrorSummary { get; init; }
    public long ElapsedMs { get; init; }
    public string CallId { get; init; } = "";
    public string? TraceId { get; init; }
    public long? DeliveryId { get; init; }
    public int? AttemptNo { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>出站目标摘要(管理界面展示,永不含秘密)。</summary>
public record OutboundTargetView
{
    public string Name { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public double TimeoutSeconds { get; init; }
    public OutboundAuthType AuthType { get; init; }

    /// <summary>自定义凭据头名(<see cref="OutboundAuthType.Header"/> 时)。</summary>
    public string? AuthHeaderName { get; init; }

    public IReadOnlyList<string> TrustedCidrs { get; init; } = [];

    /// <summary>凭据来源能否取到秘密(只回答有无,不回显)。</summary>
    public bool HasCredential { get; init; }
}

/// <summary>
/// 出站调用记录的持久化与查询。写入尽力而为(失败只告警)。
/// <para>默认实现 <see cref="OutboundLogService"/>,<c>TryAddScoped</c> 注册、方法 virtual。</para>
/// </summary>
public interface IOutboundLogService
{
    /// <summary>写入一条记录。</summary>
    Task RecordAsync(OutboundLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>分页查询(新在前)。</summary>
    Task<PagedList<OutboundLogView>> PageAsync(OutboundLogPageInput input, CancellationToken cancellationToken = default);
}
