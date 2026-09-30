using System.Linq.Expressions;
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IIntegrationRetentionService"/> 默认实现:先查一批过期行 Id 再按 Id 删除,循环到不足一批为止——
/// 每批一个短语句,不持有长事务,也不依赖各方言对 <c>DELETE ... LIMIT</c> 的不同写法。
/// </summary>
public class IntegrationRetentionService(ISqlSugarClient db, IntegrationOptions options, TimeProvider time) : IIntegrationRetentionService
{
    /// <inheritdoc />
    public virtual async Task<IntegrationRetentionResult> CleanupAsync(CancellationToken cancellationToken = default) => new()
    {
        InboundLogs = await CleanupInboundLogsAsync(cancellationToken),
        OutboundLogs = await CleanupOutboundLogsAsync(cancellationToken),
        Deliveries = await CleanupDeliveriesAsync(cancellationToken),
    };

    /// <summary>
    /// 清理已完结(成功、已取消)且完成时刻早于保留期的投递记录,连同其尝试记录(每批一个短事务)。
    /// 其他状态(待处理、处理中、待确认、待核对、耗尽、失败)永不自动删除。
    /// </summary>
    protected virtual async Task<int> CleanupDeliveriesAsync(CancellationToken cancellationToken)
    {
        var days = options.Retention.DeliveryDays;
        if (days <= 0) return 0;
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-days);
        var succeeded = DeliveryStatus.Succeeded;
        var cancelled = DeliveryStatus.Cancelled;
        var batch = Math.Max(1, options.Retention.BatchSize);
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = await db.Queryable<IntegrationDelivery>()
                .Where(d => (d.Status == succeeded || d.Status == cancelled) && d.CompletedAtUtc != null && d.CompletedAtUtc < cutoff)
                .OrderBy(d => d.Id).Take(batch).Select(d => d.Id).ToListAsync(cancellationToken);
            if (ids.Count == 0) break;
            var deleted = 0;
            var result = await db.Ado.UseTranAsync(async () =>
            {
                await db.Deleteable<IntegrationDeliveryAttempt>().Where(a => ids.Contains(a.DeliveryId)).ExecuteCommandAsync(cancellationToken);
                deleted = await db.Deleteable<IntegrationDelivery>().In(ids).ExecuteCommandAsync(cancellationToken);
            });
            if (!result.IsSuccess) throw result.ErrorException;
            total += deleted;
            if (ids.Count < batch) break;
        }
        return total;
    }

    /// <summary>清理过期开放调用记录(按记录时间,本地时间口径与审计列一致)。</summary>
    protected virtual async Task<int> CleanupInboundLogsAsync(CancellationToken cancellationToken)
    {
        var days = options.Retention.InboundLogDays;
        if (days <= 0) return 0;
        var cutoff = time.GetLocalNow().DateTime.AddDays(-days);
        return await DeleteInBatchesAsync<IntegrationInboundLog>(l => l.CreateTime < cutoff, cancellationToken);
    }

    /// <summary>清理过期出站调用记录(按记录时间)。</summary>
    protected virtual async Task<int> CleanupOutboundLogsAsync(CancellationToken cancellationToken)
    {
        var days = options.Retention.OutboundLogDays;
        if (days <= 0) return 0;
        var cutoff = time.GetLocalNow().DateTime.AddDays(-days);
        return await DeleteInBatchesAsync<IntegrationOutboundLog>(l => l.CreateTime < cutoff, cancellationToken);
    }

    /// <summary>分批删除满足条件的行,返回删除总数。</summary>
    protected async Task<int> DeleteInBatchesAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        where T : PrimaryId, new()
    {
        var batch = Math.Max(1, options.Retention.BatchSize);
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = await db.Queryable<T>().Where(predicate).OrderBy(x => x.Id).Take(batch).Select(x => x.Id).ToListAsync(cancellationToken);
            if (ids.Count == 0) break;
            total += await db.Deleteable<T>().In(ids).ExecuteCommandAsync(cancellationToken);
            if (ids.Count < batch) break;
        }
        return total;
    }
}
