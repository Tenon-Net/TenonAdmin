using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOutboundLogService"/> 默认实现:写 <c>itg_outbound_log</c>,写失败只告警。
/// <para>记录走注入的同一个 <see cref="ISqlSugarClient"/>:若调用方在数据库事务内发起出站调用,记录随该事务提交或回滚——
/// 不建议在事务内做网络调用(持锁等网络);需要与业务原子的外呼请用可靠投递。</para>
/// </summary>
public class OutboundLogService(ISqlSugarClient db, ILogger<OutboundLogService> logger) : IOutboundLogService
{
    /// <inheritdoc />
    public virtual async Task RecordAsync(OutboundLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            await db.Insertable(ToEntity(entry)).ExecuteCommandAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("出站调用记录写入失败。Target={Target} CallId={CallId} ExceptionType={ExceptionType}",
                entry.Target, entry.CallId, ex.GetType().Name);
        }
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<OutboundLogView>> PageAsync(OutboundLogPageInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var target = input.Target?.Trim();
        var operation = input.Operation?.Trim();
        var callId = input.CallId?.Trim();
        var traceId = input.TraceId?.Trim();
        var deliveryId = input.DeliveryId ?? 0;
        var outcome = input.Outcome ?? OutboundOutcome.Succeeded;
        var start = input.StartTime ?? DateTime.MinValue;
        var end = input.EndTime ?? DateTime.MaxValue;

        var page = await db.Queryable<IntegrationOutboundLog>()
            .WhereIF(!string.IsNullOrEmpty(target), l => l.Target == target)
            .WhereIF(!string.IsNullOrEmpty(operation), l => l.Operation!.Contains(operation!))
            .WhereIF(input.Outcome.HasValue, l => l.Outcome == outcome)
            .WhereIF(input.DeliveryId.HasValue, l => l.DeliveryId == deliveryId)
            .WhereIF(!string.IsNullOrEmpty(callId), l => l.CallId == callId)
            .WhereIF(!string.IsNullOrEmpty(traceId), l => l.TraceId == traceId)
            .WhereIF(input.StartTime.HasValue, l => l.CreateTime >= start)
            .WhereIF(input.EndTime.HasValue, l => l.CreateTime <= end)
            .OrderBy(l => l.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

        return new PagedList<OutboundLogView>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items.Select(ToView).ToList(),
        };
    }

    /// <summary>实体 → 视图。</summary>
    protected virtual OutboundLogView ToView(IntegrationOutboundLog l) => new()
    {
        Id = l.Id,
        Target = l.Target,
        Operation = l.Operation,
        HttpMethod = l.HttpMethod,
        Url = l.Url,
        StatusCode = l.StatusCode,
        Outcome = l.Outcome,
        Reason = l.Reason,
        ErrorSummary = l.ErrorSummary,
        ElapsedMs = l.ElapsedMs,
        CallId = l.CallId,
        TraceId = l.TraceId,
        DeliveryId = l.DeliveryId,
        AttemptNo = l.AttemptNo,
        CreateTime = l.CreateTime,
    };

    /// <summary>条目 → 实体(字段按列宽截断)。</summary>
    protected virtual IntegrationOutboundLog ToEntity(OutboundLogEntry entry) => new()
    {
        Target = IntegrationText.Truncate(entry.Target, 64) ?? "",
        Operation = IntegrationText.Truncate(entry.Operation, 64),
        HttpMethod = IntegrationText.Truncate(entry.HttpMethod, 16) ?? "",
        Url = IntegrationText.Truncate(entry.Url, 512) ?? "",
        StatusCode = entry.StatusCode,
        Outcome = entry.Outcome,
        Reason = IntegrationText.Truncate(entry.Reason, 64),
        ErrorSummary = IntegrationText.Truncate(entry.ErrorSummary, 512),
        ElapsedMs = entry.ElapsedMs,
        CallId = IntegrationText.Truncate(entry.CallId, 64) ?? "",
        TraceId = IntegrationText.Truncate(entry.TraceId, 64),
        DeliveryId = entry.DeliveryId,
        AttemptNo = entry.AttemptNo,
    };
}
