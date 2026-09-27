using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Workflow;

/// <summary>
/// outbox 后台扫描任务:复用内核 <see cref="IAdminJob"/>,扫描可领取消息并逐项交给
/// <see cref="WfOutboxDispatcher"/>。真正的单赢家由 AttemptCount CAS 决定。
/// 本任务不扫描、不领取 <c>wf_node_execution</c>。
/// </summary>
public class WfOutboxJob(
    ISqlSugarClient db,
    WfOutboxDispatcher dispatcher,
    WorkflowOptions options,
    TimeProvider time,
    ILogger<WfOutboxJob> logger) : IAdminJob
{
    /// <inheritdoc />
    public virtual async Task ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = time.GetUtcNow().UtcDateTime;
        var candidates = await ScanAsync(
            nowUtc,
            Math.Max(1, options.OutboxScanBatchSize),
            cancellationToken);

        foreach (var row in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await dispatcher.RunAsync(row.Id, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "工作流 outbox 派发失败。OutboxId={OutboxId} ExceptionType={ExceptionType}",
                    row.Id,
                    ex.GetType().Name);
                context.Log?.Invoke(
                    $"工作流 outbox {row.Id} 派发失败(异常类型:{ex.GetType().Name})。");
            }
        }
    }

    /// <summary>
    /// 扫描候选:Pending 或可见性已到期的 Dispatching。实际领取仍由 dispatcher 条件 CAS。
    /// </summary>
    protected virtual Task<List<WfOutbox>> ScanAsync(
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = WfOutboxStatus.Pending;
        var dispatching = WfOutboxStatus.Dispatching;
        return db.Queryable<WfOutbox>()
            .Where(o => (o.Status == pending || o.Status == dispatching) && o.AvailableAtUtc <= nowUtc)
            .OrderBy(o => o.Id, OrderByType.Asc)
            .Take(take > 0 ? take : 1)
            .ToListAsync();
    }
}
