using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Workflow;

/// <summary>
/// 节点 execution 后台扫描任务：复用内核既有 <see cref="IAdminJob"/> 调度体系，扫描可领取的
/// execution 并逐项交给 <see cref="WfNodeExecutionDispatcher"/>。本类不复制 claim、handler 调用或
/// 结果回写状态机；真正的单赢家由 dispatcher 的 lease/fence CAS 决定。
/// <para>
/// 候选状态只有三类：新建的 <see cref="WfNodeExecutionStatus.Pending"/>、到期的
/// <see cref="WfNodeExecutionStatus.RetryScheduled"/> 和租约过期的
/// <see cref="WfNodeExecutionStatus.Running"/>。未来 retry 与仍有效的 lease 不进入本拍。
/// </para>
/// <para>
/// 每一项独立捕获非取消异常，避免一条损坏 execution 拖垮整拍；外部取消直接传播给 dispatcher，
/// 由宿主停机/任务 kill 语义处理。异常日志只写 execution Id 与异常类型，避免把 handler 正文复制
/// 进任务消息。
/// </para>
/// </summary>
public class WfNodeExecutionJob(
    ISqlSugarClient db,
    WfNodeExecutionDispatcher dispatcher,
    JobExecutor executor,
    WorkflowOptions options,
    TimeProvider time,
    ILogger<WfNodeExecutionJob> logger) : IAdminJob
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
            Math.Max(1, options.NodeExecutionScanBatchSize),
            cancellationToken);
        var leaseDuration = TimeSpan.FromSeconds(Math.Max(1, options.NodeExecutionLeaseSeconds));

        foreach (var execution in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await dispatcher.RunAsync(
                    execution.Id,
                    executor.InstanceId,
                    leaseDuration,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单项隔离：execution 自身会保留 Running/lease 状态，下一拍可按 lease 规则恢复；
                // handler 未分类异常的有限收敛由 dispatcher 的 handler 边界负责，不在此处伪造结果。
                logger.LogError(
                    ex,
                    "工作流节点 execution 派发失败。ExecutionId={ExecutionId} ExceptionType={ExceptionType}",
                    execution.Id,
                    ex.GetType().Name);
                context.Log?.Invoke(
                    $"工作流节点 execution {execution.Id} 派发失败(异常类型:{ex.GetType().Name})。");
            }
        }
    }

    /// <summary>
    /// 扫描候选 execution。查询只读、使用应用时间参数和普通枚举谓词，数据库差异留给 SqlSugar。
    /// 实际领取仍由 dispatcher 再次执行条件 CAS，因此扫描快照过期或被其他副本抢先时不会产生双执行。
    /// </summary>
    protected virtual Task<List<WfNodeExecution>> ScanAsync(
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return db.Queryable<WfNodeExecution>()
            .Where(e => e.Status == WfNodeExecutionStatus.Pending
                     || (e.Status == WfNodeExecutionStatus.RetryScheduled && e.NextRetryAtUtc <= nowUtc)
                     || (e.Status == WfNodeExecutionStatus.Running && e.LeaseExpiresAtUtc < nowUtc))
            .OrderBy(e => e.Id, OrderByType.Asc)
            .Take(take > 0 ? take : 1)
            .ToListAsync();
    }
}
