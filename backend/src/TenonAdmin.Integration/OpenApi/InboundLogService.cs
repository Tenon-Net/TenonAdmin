using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary><see cref="IInboundLogService"/> 默认实现:写 <c>itg_inbound_log</c>,写失败只告警(与内核操作日志同一策略)。</summary>
public class InboundLogService(ISqlSugarClient db, ILogger<InboundLogService> logger) : IInboundLogService
{
    /// <inheritdoc />
    public virtual async Task RecordAsync(InboundLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            await db.Insertable(ToEntity(entry)).ExecuteCommandAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "开放调用记录写入失败。Route={Route} TraceId={TraceId}", entry.Route, entry.TraceId);
        }
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<InboundLogView>> PageAsync(InboundLogPageInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var route = input.Route?.Trim();
        var traceId = input.TraceId?.Trim();
        var requestId = input.ClientRequestId?.Trim();
        var appId = input.AppId ?? 0;
        var outcome = input.Outcome ?? InboundCallOutcome.Succeeded;
        var start = input.StartTime ?? DateTime.MinValue;
        var end = input.EndTime ?? DateTime.MaxValue;

        var page = await db.Queryable<IntegrationInboundLog>()
            .WhereIF(input.AppId.HasValue, l => l.AppId == appId)
            .WhereIF(!string.IsNullOrEmpty(route), l => l.Route.Contains(route!))
            .WhereIF(input.Outcome.HasValue, l => l.Outcome == outcome)
            .WhereIF(!string.IsNullOrEmpty(traceId), l => l.TraceId == traceId)
            .WhereIF(!string.IsNullOrEmpty(requestId), l => l.ClientRequestId == requestId)
            .WhereIF(input.StartTime.HasValue, l => l.CreateTime >= start)
            .WhereIF(input.EndTime.HasValue, l => l.CreateTime <= end)
            .OrderBy(l => l.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

        return new PagedList<InboundLogView>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items.Select(ToView).ToList(),
        };
    }

    /// <summary>实体 → 视图。</summary>
    protected virtual InboundLogView ToView(IntegrationInboundLog l) => new()
    {
        Id = l.Id,
        AppId = l.AppId,
        AppCode = l.AppCode,
        CredentialId = l.CredentialId,
        KeyId = l.KeyId,
        HttpMethod = l.HttpMethod,
        Route = l.Route,
        Path = l.Path,
        StatusCode = l.StatusCode,
        ResultCode = l.ResultCode,
        Outcome = l.Outcome,
        FailureReason = l.FailureReason,
        ElapsedMs = l.ElapsedMs,
        TraceId = l.TraceId,
        ClientRequestId = l.ClientRequestId,
        ClientIp = l.ClientIp,
        CreateTime = l.CreateTime,
    };

    /// <summary>条目 → 实体(字段按列宽截断,外部输入不信任长度)。</summary>
    protected virtual IntegrationInboundLog ToEntity(InboundLogEntry entry) => new()
    {
        AppId = entry.AppId,
        AppCode = IntegrationText.Truncate(entry.AppCode, 96),
        CredentialId = entry.CredentialId,
        KeyId = IntegrationText.Truncate(entry.KeyId, 32),
        HttpMethod = IntegrationText.Truncate(entry.HttpMethod, 16) ?? "",
        Route = IntegrationText.Truncate(entry.Route, 256) ?? "",
        Path = IntegrationText.Truncate(entry.Path, 512) ?? "",
        StatusCode = entry.StatusCode,
        ResultCode = entry.ResultCode,
        Outcome = entry.Outcome,
        FailureReason = IntegrationText.Truncate(entry.FailureReason, 64),
        ElapsedMs = entry.ElapsedMs,
        TraceId = IntegrationText.Truncate(entry.TraceId, 64) ?? "",
        ClientRequestId = IntegrationText.Truncate(entry.ClientRequestId, 64),
        ClientIp = IntegrationText.Truncate(entry.ClientIp, 64),
    };
}
