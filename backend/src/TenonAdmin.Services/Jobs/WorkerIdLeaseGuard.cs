using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 雪花 WorkerId 数据库租约守卫(QA27):启动时争抢租约,周期续租,停止时释放。
/// 同一 WorkerId 不能被两个活跃实例同时持有——第二个启动的实例在 <see cref="StartAsync"/> 抛异常,
/// 把静默的主键冲突换成一条可读的启动错误。
/// <para>直接构造时保留上述领取行为；Services 的托管注册延迟解析最终发号器，只校验并释放其已领取的租约。</para>
/// <para>租约 TTL = <see cref="AdminJobsOptions.HeartbeatSeconds"/> × 3;续租间隔 = HeartbeatSeconds / 2。</para>
/// <para>被强杀的进程(调试停止、kill -9、容器重启)来不及释放租约,重启会撞上自己的残留租约。
/// 这种情况下守卫按 <see cref="OwnerProcessIsGone"/> 判定持有者是否还活着,已退出则直接接管,
/// 不必干等一个 TTL。</para>
/// </summary>
public sealed class WorkerIdLeaseGuard : IHostedLifecycleService, IDisposable
{
    private readonly IServiceProvider? _services;
    private readonly ISqlSugarClient? _db;
    private readonly AdminIdOptions? _idOptions;
    private readonly AdminJobsOptions? _jobsOptions;
    private readonly ILogger<WorkerIdLeaseGuard>? _logger;
    private readonly WorkerIdAssignment? _assignment;
    private readonly TimeProvider _time;
    private string _instanceToken = Guid.NewGuid().ToString("N")[..8];
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private IWorkerIdLeaseLifetime? _lifetime;
    private bool _startingCompleted;
    private bool _startCompleted;
    private bool _stopped;
    private long _claimStartedAt;
    private int _workerId;
    private string _nodeName = "";

    /// <summary>保留给直接使用者的原有构造函数；其 Start/Stop 继续管理指定数据库上的租约。</summary>
    public WorkerIdLeaseGuard(
        ISqlSugarClient db,
        AdminIdOptions idOptions,
        AdminJobsOptions jobsOptions,
        ILogger<WorkerIdLeaseGuard> logger,
        TimeProvider? time = null,
        WorkerIdAssignment? assignment = null)
    {
        _db = db;
        _idOptions = idOptions;
        _jobsOptions = jobsOptions;
        _logger = logger;
        _assignment = assignment;
        _time = time ?? TimeProvider.System;
    }

    internal WorkerIdLeaseGuard(IServiceProvider services)
    {
        _services = services;
        _time = TimeProvider.System;
    }

    internal static WorkerIdLeaseGuard CreateHosted(IServiceProvider services) => new(services);

    private ISqlSugarClient DirectDb => _db
        ?? throw new InvalidOperationException("延迟宿主模式不能执行旧版数据库领取路径。");
    private AdminIdOptions DirectIdOptions => _idOptions
        ?? throw new InvalidOperationException("延迟宿主模式没有直接 WorkerId 配置。");
    private AdminJobsOptions DirectJobsOptions => _jobsOptions
        ?? throw new InvalidOperationException("延迟宿主模式没有直接任务配置。");
    private ILogger<WorkerIdLeaseGuard> DirectLogger => _logger
        ?? throw new InvalidOperationException("延迟宿主模式没有直接租约日志器。");

    private int HeartbeatSeconds => Math.Max(DirectJobsOptions.HeartbeatSeconds, 2);
    private TimeSpan LeaseTtl => TimeSpan.FromSeconds(HeartbeatSeconds * 3);

    // Host 的 Starting 阶段先于任何普通 HostedService.StartAsync；先领取并启动续租，普通 Start 再在 DDL 后确认。
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfStopped();
            if (_startingCompleted)
                return;

            await StartCoreAsync(cancellationToken);
            _startingCompleted = true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfStopped();
            if (_startCompleted)
                return;

            // 直接调用旧版 StartAsync 时仍一次完成领取；Host 则已经在 StartingAsync 完成此步。
            if (!_startingCompleted)
            {
                await StartCoreAsync(cancellationToken);
                _startingCompleted = true;
            }

            // SqlSugar 的 DatabaseInitializer 在 Services 注册之前，故普通 Start 到这里时完整 DDL 已结束。
            // 默认数据库 lifetime 强制做 fresh owner CAS；消费者自定义 lifetime 只沿用公开的本地校验契约。
            if (_lifetime is DbWorkerIdLeaseLifetime databaseLifetime)
                await databaseLifetime.ConfirmFreshAsync(cancellationToken);
            _lifetime?.EnsureActive();
            _startCompleted = true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_services is not null)
        {
            StartHostedMode();
            return;
        }

        if (_assignment?.Lifetime is { } existingLifetime)
        {
            if (existingLifetime is not DbWorkerIdLeaseLifetime)
                existingLifetime.EnsureActive();
            _lifetime = existingLifetime;
            return;
        }

        _workerId = (int)(_assignment?.WorkerId ?? DirectIdOptions.WorkerId ?? 0);
        if (!string.IsNullOrEmpty(_assignment?.InstanceToken))
            _instanceToken = _assignment.InstanceToken;
        var machineName = Environment.MachineName;
        _nodeName = $"{machineName}#{_workerId}@{_instanceToken}";
        var pid = Environment.ProcessId;

        DirectDb.CodeFirst.InitTables<SysWorkerLease>();
        for (var attempt = 1; !await TryClaimAsync(machineName, pid); attempt++)
        {
            if (attempt >= 2)
                throw new InvalidOperationException(
                    $"WorkerId {_workerId} 的租约被其它节点连续抢占，本实例放弃启动。" +
                    "请为本实例配置不同的 TenonAdmin:Id:WorkerId。");
        }

        var lifetime = new DbWorkerIdLeaseLifetime(
            DirectDb, _workerId, _nodeName, DirectJobsOptions, _time, _claimStartedAt, logger: DirectLogger);
        try
        {
            // 直接领取可能被锁拖过本地确认期限；先新鲜确认，再满足 AttachLifetime 的有效性前置条件。
            if (_assignment is not null)
                await lifetime.ConfirmFreshAsync(cancellationToken);
            _assignment?.AttachLifetime(lifetime);
            _lifetime = lifetime;
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
        DirectLogger.LogInformation("WorkerId {WorkerId} 租约已获取(node={Node}, pid={Pid}, ttl={Ttl}s)。",
            _workerId, _nodeName, pid, LeaseTtl.TotalSeconds);
    }

    private void ThrowIfStopped()
    {
        if (_stopped)
            throw new InvalidOperationException("WorkerId 守卫已停止，不能重新启动已释放的发号器。");
    }

    private void StartHostedMode()
    {
        var services = _services
            ?? throw new InvalidOperationException("直接模式不能执行延迟宿主启动路径。");
        var generator = services.GetRequiredService<IIdGenerator>();
        if (generator is not SnowflakeIdGenerator { Assignment: { } assignment })
            return;

        _lifetime = assignment.Lifetime
            ?? throw new InvalidOperationException("默认雪花发号器尚未附加 WorkerId 数据库租约。");
        // 默认租约可能在 Host.Start 前已暂停，留到普通 Start 做新鲜确认；自定义契约仍立即校验。
        if (_lifetime is not DbWorkerIdLeaseLifetime)
            assignment.EnsureActive();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // 即使宿主的停机 token 已取消，也要让默认发号器立即失效并释放自有续租任务。
        await _startGate.WaitAsync();
        try
        {
            _stopped = true;
            Interlocked.Exchange(ref _lifetime, null)?.Dispose();
        }
        finally { _startGate.Release(); }
    }

    /// <summary>
    /// 争抢一次租约。<c>false</c> = 落库时那一行已被别人抢先插入或改掉,应当重读再试;
    /// 号确实被活着的实例占着则直接抛,不在重试范围内。
    /// </summary>
    private async Task<bool> TryClaimAsync(string machineName, int pid)
    {
        _claimStartedAt = _time.GetTimestamp();
        var now = DbWorkerIdLeaseLifetime.DatabaseNow(DirectDb);
        var expiresAt = now + LeaseTtl;

        var existing = await DirectDb.Queryable<SysWorkerLease>()
            .Where(l => l.WorkerId == _workerId)
            .FirstAsync();

        if (existing is null)
        {
            try
            {
                await DirectDb.Insertable(new SysWorkerLease
                {
                    // 租约确认前默认发号器处于 pending；租约表使用固定槽主键，不能触发审计发号。
                    Id = _workerId + 1,
                    WorkerId = _workerId,
                    NodeName = _nodeName,
                    MachineName = machineName,
                    Pid = pid,
                    LeaseExpiresAt = expiresAt,
                }).ExecuteCommandAsync();
                return true;
            }
            catch (Exception ex)
            {
                // WorkerId 上有唯一索引:两个实例同时读到「没有这一行」时,只有一个插得进去。
                // 输的那个不该把主键冲突原样抛给启动流程 —— 重读确认确实是撞行,再回 false 走上面的重试。
                // 驱动各自的异常类型不通用,所以不嗅探异常码,改用「行是否已存在」判定。
                if (!await LeaseRowExistsAsync())
                {
                    throw;
                }

                DirectLogger.LogDebug(ex, "WorkerId {WorkerId} 租约插入撞行,重读后再判。", _workerId);
                return false;
            }
        }

        if (existing.LeaseExpiresAt > now && existing.NodeName != _nodeName)
        {
            if (!OwnerProcessIsGone(existing))
            {
                var remaining = Math.Max(1, (int)Math.Ceiling((existing.LeaseExpiresAt - now).TotalSeconds));
                throw new InvalidOperationException(
                    $"WorkerId {_workerId} 已被节点 \"{existing.NodeName}\"" +
                    $"(pid={existing.Pid}, 主机={OwnerMachine(existing)})租约持有" +
                    $"(到期 {existing.LeaseExpiresAt:O},约 {remaining}s 后)。" +
                    "多实例部署请为本实例配置不同的 TenonAdmin:Id:WorkerId;" +
                    "若持有者是另一台主机上已被强制结束的进程,等租约到期后重试即可。");
            }

            DirectLogger.LogWarning(
                "WorkerId {WorkerId} 上有节点 \"{Node}\"(pid={Pid})的残留租约,该进程已退出,本实例直接接管。",
                _workerId, existing.NodeName, existing.Pid);
        }

        // 条件更新代替读-改-写:两个实例同时判定「可以接管」时,只有先落库的那个 rows=1,
        // 另一个 rows=0 → 重读再判,而不是双双认为自己拿到了号。
        var rows = await DirectDb.Updateable<SysWorkerLease>()
            .SetColumns(l => new SysWorkerLease
            {
                NodeName = _nodeName,
                MachineName = machineName,
                Pid = pid,
                LeaseExpiresAt = expiresAt,
            })
            .Where(l => l.WorkerId == _workerId && l.NodeName == existing.NodeName
                        && l.LeaseExpiresAt == existing.LeaseExpiresAt)
            .ExecuteCommandAsync();

        return rows > 0;
    }

    private async Task<bool> LeaseRowExistsAsync() =>
        await DirectDb.Queryable<SysWorkerLease>().Where(l => l.WorkerId == _workerId).AnyAsync();

    /// <summary>
    /// 判断租约持有者进程是否已经不在了。跨主机的 pid 没有可比性,因此主机名不同一律当作「还活着」。
    /// </summary>
    /// <remarks>
    /// 只会把「已死」误判成「还活着」(pid 被复用时),不会反过来——前者最多多等一个 TTL,
    /// 后者会放两个实例同时发号。所以每个查不准的分支都返回 false。
    /// </remarks>
    private static bool OwnerProcessIsGone(SysWorkerLease lease)
    {
        if (lease.Pid <= 0
            || !string.Equals(OwnerMachine(lease), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var owner = Process.GetProcessById(lease.Pid);
            return owner.HasExited;
        }
        catch (ArgumentException)
        {
            return true;    // 该 pid 上没有进程
        }
        catch (InvalidOperationException)
        {
            return true;    // 查询期间进程已退出
        }
        catch (Exception)
        {
            return false;   // 权限不足 / 平台不支持等查不准的情况,保守处理
        }
    }

    /// <summary>
    /// 取租约持有者的主机名。<see cref="SysWorkerLease.MachineName"/> 是后加的列,升级前写下的行没有,
    /// 退回从 <see cref="SysWorkerLease.NodeName"/> 前缀取——格式由本类写死为 <c>{主机名}#{WorkerId}@{token}</c>。
    /// </summary>
    private static string OwnerMachine(SysWorkerLease lease)
    {
        if (!string.IsNullOrEmpty(lease.MachineName))
        {
            return lease.MachineName;
        }

        var hash = lease.NodeName.IndexOf('#');
        return hash > 0 ? lease.NodeName[..hash] : "";
    }

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();
}
