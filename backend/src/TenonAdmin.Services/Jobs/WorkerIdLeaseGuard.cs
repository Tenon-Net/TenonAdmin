using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 雪花 WorkerId 数据库租约守卫(QA27):启动时争抢租约,周期续租,停止时释放。
/// 同一 WorkerId 不能被两个活跃实例同时持有——第二个启动的实例在 <see cref="StartAsync"/> 抛异常,
/// 把静默的主键冲突换成一条可读的启动错误。
/// <para>租约 TTL = <see cref="AdminJobsOptions.HeartbeatSeconds"/> × 3;续租间隔 = HeartbeatSeconds / 2。</para>
/// </summary>
public sealed class WorkerIdLeaseGuard(
    ISqlSugarClient db,
    AdminIdOptions idOptions,
    AdminJobsOptions jobsOptions,
    ILogger<WorkerIdLeaseGuard> logger,
    TimeProvider? time = null) : IHostedService, IDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly string _instanceToken = Guid.NewGuid().ToString("N")[..8];
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private int _workerId;
    private string _nodeName = "";

    private DateTime Now => _time.GetLocalNow().DateTime;
    private int HeartbeatSeconds => Math.Max(jobsOptions.HeartbeatSeconds, 2);
    private TimeSpan LeaseTtl => TimeSpan.FromSeconds(HeartbeatSeconds * 3);
    private TimeSpan RenewInterval => TimeSpan.FromSeconds(HeartbeatSeconds / 2.0);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _workerId = idOptions.WorkerId ?? 0;
        _nodeName = $"{Environment.MachineName}#{_workerId}@{_instanceToken}";
        var pid = Environment.ProcessId;
        var now = Now;
        var expiresAt = now + LeaseTtl;

        db.CodeFirst.InitTables<SysWorkerLease>();

        var existing = await db.Queryable<SysWorkerLease>()
            .Where(l => l.WorkerId == _workerId)
            .FirstAsync();

        if (existing is not null)
        {
            if (existing.LeaseExpiresAt > now && existing.NodeName != _nodeName)
            {
                throw new InvalidOperationException(
                    $"WorkerId {_workerId} 已被节点 \"{existing.NodeName}\"(pid={existing.Pid})租约持有" +
                    $"(到期 {existing.LeaseExpiresAt:O})。请为本实例配置不同的 TenonAdmin:Id:WorkerId。");
            }

            existing.NodeName = _nodeName;
            existing.Pid = pid;
            existing.LeaseExpiresAt = expiresAt;
            await db.Updateable(existing).ExecuteCommandAsync();
        }
        else
        {
            await db.Insertable(new SysWorkerLease
            {
                WorkerId = _workerId,
                NodeName = _nodeName,
                Pid = pid,
                LeaseExpiresAt = expiresAt,
            }).ExecuteCommandAsync();
        }

        logger.LogInformation("WorkerId {WorkerId} 租约已获取(node={Node}, pid={Pid}, ttl={Ttl}s)。",
            _workerId, _nodeName, pid, LeaseTtl.TotalSeconds);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _heartbeatTask = HeartbeatLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_heartbeatTask is not null)
            {
                try { await _heartbeatTask; } catch (OperationCanceledException) { }
            }
        }

        try
        {
            await db.Deleteable<SysWorkerLease>()
                .Where(l => l.WorkerId == _workerId && l.NodeName == _nodeName)
                .ExecuteCommandAsync();
            logger.LogInformation("WorkerId {WorkerId} 租约已释放。", _workerId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WorkerId {WorkerId} 租约释放失败(进程退出后租约将自然过期)。", _workerId);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RenewInterval, _time, ct);
                var expiresAt = Now + LeaseTtl;
                await db.Updateable<SysWorkerLease>()
                    .SetColumns(l => new SysWorkerLease { LeaseExpiresAt = expiresAt })
                    .Where(l => l.WorkerId == _workerId && l.NodeName == _nodeName)
                    .ExecuteCommandAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WorkerId {WorkerId} 租约续租失败,下轮重试。", _workerId);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
