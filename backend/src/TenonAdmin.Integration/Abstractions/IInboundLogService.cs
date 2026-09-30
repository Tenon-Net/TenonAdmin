using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>一条待写入的开放调用记录(不含任何正文与认证头)。</summary>
public sealed record InboundLogEntry
{
    public long? AppId { get; init; }
    public string? AppCode { get; init; }
    public long? CredentialId { get; init; }
    public string? KeyId { get; init; }
    public string HttpMethod { get; init; } = "";
    public string Route { get; init; } = "";
    public string Path { get; init; } = "";
    public int StatusCode { get; init; }
    public int ResultCode { get; init; }
    public InboundCallOutcome Outcome { get; init; }
    public string? FailureReason { get; init; }
    public long ElapsedMs { get; init; }
    public string TraceId { get; init; } = "";
    public string? ClientRequestId { get; init; }
    public string? ClientIp { get; init; }
}

/// <summary>开放调用记录分页查询入参(全部可选;时间按记录时间,本地时间口径与内核日志一致)。</summary>
public record InboundLogPageInput : PageInputBase
{
    public long? AppId { get; init; }

    /// <summary>端点权限码关键字(模糊)。</summary>
    public string? Route { get; init; }

    public InboundCallOutcome? Outcome { get; init; }

    /// <summary>追踪标识(精确)。</summary>
    public string? TraceId { get; init; }

    /// <summary>调用方请求标识(精确)。</summary>
    public string? ClientRequestId { get; init; }

    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
}

/// <summary>开放调用记录视图(与实体同字段;不含任何正文或认证头——实体本就不存)。</summary>
public record InboundLogView
{
    public long Id { get; init; }
    public long? AppId { get; init; }
    public string? AppCode { get; init; }
    public long? CredentialId { get; init; }
    public string? KeyId { get; init; }
    public string HttpMethod { get; init; } = "";
    public string Route { get; init; } = "";
    public string Path { get; init; } = "";
    public int StatusCode { get; init; }
    public int ResultCode { get; init; }
    public InboundCallOutcome Outcome { get; init; }
    public string? FailureReason { get; init; }
    public long ElapsedMs { get; init; }
    public string TraceId { get; init; } = "";
    public string? ClientRequestId { get; init; }
    public string? ClientIp { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>
/// 开放调用记录的持久化(实现契约 §5)。写入尽力而为:失败只记告警,不影响已产出的响应。
/// <para>默认实现 <see cref="InboundLogService"/>,<c>TryAddScoped</c> 注册、方法 virtual;前置注册同接口即可改写到别处(如日志平台)。</para>
/// </summary>
public interface IInboundLogService
{
    /// <summary>写入一条调用记录。</summary>
    Task RecordAsync(InboundLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>分页查询(新在前)。</summary>
    Task<PagedList<InboundLogView>> PageAsync(InboundLogPageInput input, CancellationToken cancellationToken = default);
}
