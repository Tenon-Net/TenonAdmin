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

        // 自有 CTS,不要 CreateLinkedTokenSource(StartAsync 的 token):
        // 宿主停机时会先 Dispose 启动链上的 token source,再调 StopAsync——
        // 链到它的 CTS 已被 Dispose,CancelAsync 会抛 ObjectDisposedException,拖垮整批 WebApplicationFactory 测试。
        _cts = new CancellationTokenSource();
        _heartbeatTask = HeartbeatLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            try { await cts.CancelAsync(); }
            catch (ObjectDisposedException) { /* already torn down */ }

            if (_heartbeatTask is not null)
            {
                // 循环内部已吞掉所有续租异常,唯一还能逃出来的是它那句告警日志本身
                // (停机期日志提供者可能已释放,同 StopAsync 尾部)。停机不该被它拖垮。
                try { await _heartbeatTask; }
                catch { /* 取消或日志提供者已释放 */ }
            }

            cts.Dispose();
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
            // 释放是尽力而为:租约到期会自然回收。停机期日志提供者可能已被释放(Windows EventLog
            // 就会抛 ObjectDisposedException),所以告警本身也要兜住——任何异常冒出 StopAsync,
            // Host.StopAsync 都会聚合抛出,连带拖垮正在销毁的 WebApplicationFactory。
            try
            {
                logger.LogWarning(ex, "WorkerId {WorkerId} 租约释放失败(进程退出后租约将自然过期)。", _workerId);
            }
            catch { /* 日志提供者已随宿主停机释放,无处可报 */ }
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
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Dispose();
    }
}
