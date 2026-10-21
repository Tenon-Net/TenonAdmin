using System.Globalization;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 默认数据库领槽的完整生命周期：从领取成功起立即续租，并在最后一次确认期限后拒绝继续发号。
/// 每个实例使用独立的裸 SqlSugar 客户端，避免续租与业务请求共享连接或 AsyncLocal 上下文。
/// </summary>
internal sealed class DbWorkerIdLeaseLifetime : IWorkerIdLeaseLifetime
{
    private enum LeaseState
    {
        Active,
        Suspended,
        Lost,
        Disposed,
    }

    private enum ConfirmationResult
    {
        Confirmed,
        RetryFresh,
        Terminal,
    }

    private readonly ISqlSugarClient _db;
    private readonly int _workerId;
    private readonly string _nodeName;
    private readonly TimeProvider _time;
    private readonly TimeSpan _leaseTtl;
    private readonly TimeSpan _localConfirmedTtl;
    private readonly TimeSpan _renewInterval;
    private readonly bool _ownsClient;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _confirmationGate = new(1, 1);
    private readonly object _stateSync = new();
    private readonly Task _renewTask;

    private long _confirmedStartedAt;
    private LeaseState _state;

    public DbWorkerIdLeaseLifetime(
        ISqlSugarClient db,
        int workerId,
        string nodeName,
        AdminJobsOptions jobs,
        TimeProvider time,
        long confirmedStartedAt,
        bool ownsClient = false,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrEmpty(nodeName);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(time);

        _db = db;
        _workerId = workerId;
        _nodeName = nodeName;
        _time = time;
        var heartbeatSeconds = Math.Max(jobs.HeartbeatSeconds, 2);
        _leaseTtl = TimeSpan.FromSeconds(heartbeatSeconds * 3);
        // 四种数据库的 CURRENT_TIMESTAMP 默认都可能只有秒级精度。数据库时间在当前秒内被
        // 向下截断时，落库到期点最多比本地单调时钟推算早近一秒，因此本地期限保守减一秒。
        _localConfirmedTtl = _leaseTtl - TimeSpan.FromSeconds(1);
        _renewInterval = TimeSpan.FromSeconds(heartbeatSeconds / 2.0);
        _ownsClient = ownsClient;
        _logger = logger;
        _confirmedStartedAt = confirmedStartedAt;

        // SqlSugarScope 与故障注入器都可能使用 AsyncLocal；续租必须拥有干净的执行上下文。
        if (ExecutionContext.IsFlowSuppressed())
        {
            _renewTask = Task.Run(RenewLoopAsync);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                _renewTask = Task.Run(RenewLoopAsync);
            }
        }
    }

    /// <inheritdoc />
    public void EnsureActive()
    {
        // 发号热路径只能读取本地状态，不能在业务事务内再开独立连接确认租约，否则会与调用者自锁。
        lock (_stateSync)
        {
            SuspendIfPastConfirmedDeadlineLocked();
            if (_state == LeaseState.Disposed)
                throw new InvalidOperationException("WorkerId 数据库租约已释放，拒绝继续发号。");
            if (_state != LeaseState.Active)
                throw new InvalidOperationException("WorkerId 数据库租约已丢失或过期，拒绝继续发号。");
        }
    }

    /// <summary>
    /// 强制完成一次新鲜的 owner CAS。宿主在 CodeFirst 事务结束后调用，避免启动期 DDL 阻塞续租后，
    /// 第一个业务 HostedService 在后台下一轮确认前先尝试发号。
    /// </summary>
    internal async Task ConfirmFreshAsync(CancellationToken cancellationToken)
    {
        // 第一次请求可能在 Active 时发出，却在旧确认期限后才返回。它只能把状态推进到 Suspended；
        // 第二次请求是在已经观察到 Suspended 后发出的 fresh CAS，才有资格恢复 Active。
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await ConfirmOnceAsync(cancellationToken);
            if (result == ConfirmationResult.Confirmed)
                return;
            if (result == ConfirmationResult.Terminal)
                break;
        }

        EnsureActive();
    }

    private async Task RenewLoopAsync()
    {
        while (!IsTerminal())
        {
            try
            {
                await Task.Delay(_renewInterval, _time, _cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (await ConfirmOnceAsync(_cts.Token) == ConfirmationResult.Terminal)
                    return;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 短暂故障不会直接判定所有权丢失；日志只写异常类型，避免泄漏连接信息。
                SafeLogWarning(
                    "WorkerId {WorkerId} 数据库租约续租失败({ErrorType})；确认期限过后将暂停发号并继续确认。",
                    _workerId,
                    ex.GetType().Name);
                SuspendIfPastConfirmedDeadline();
            }
        }
    }

    private async Task<ConfirmationResult> ConfirmOnceAsync(CancellationToken cancellationToken)
    {
        await _confirmationGate.WaitAsync(cancellationToken);
        try
        {
            long attemptStartedAt;
            bool recoveryAttempt;
            lock (_stateSync)
            {
                if (_state is LeaseState.Lost or LeaseState.Disposed)
                    return ConfirmationResult.Terminal;

                SuspendIfPastConfirmedDeadlineLocked();
                recoveryAttempt = _state == LeaseState.Suspended;
                // 起点必须在 DB 时钟查询前；查询和更新耗时都消耗本轮本地确认 TTL。
                attemptStartedAt = _time.GetTimestamp();
            }

            var databaseNow = DatabaseNow(_db);
            var expiresAt = databaseNow + _leaseTtl;
            var rows = _db.Updateable<SysWorkerLease>()
                .SetColumns(l => new SysWorkerLease { LeaseExpiresAt = expiresAt })
                .Where(l => l.WorkerId == _workerId && l.NodeName == _nodeName)
                .ExecuteCommand();

            // MySQL UseAffectedRows=true 对同值更新返回 0。回读必须同时证明 owner 未变且期限
            // 已达到本轮目标，不能仅凭 owner 相同接受一条没有续期的行。回读耗时也计入确认期限。
            var confirmed = rows != 0 || _db.Queryable<SysWorkerLease>()
                .Any(l => l.WorkerId == _workerId && l.NodeName == _nodeName
                    && l.LeaseExpiresAt >= expiresAt);
            var attemptFinishedAt = _time.GetTimestamp();

            if (!confirmed)
            {
                lock (_stateSync)
                {
                    if (_state is LeaseState.Lost or LeaseState.Disposed)
                        return ConfirmationResult.Terminal;
                    _state = LeaseState.Lost;
                }

                // 消费者 logger 可能执行任意代码，不能放在状态锁内。
                SafeLogWarning("WorkerId {WorkerId} 的数据库租约已被接管，停止续租。", _workerId);
                return ConfirmationResult.Terminal;
            }

            lock (_stateSync)
            {
                if (_state is LeaseState.Lost or LeaseState.Disposed)
                    return ConfirmationResult.Terminal;

                // 先用旧确认推进 Suspended；Active 时发出的迟到响应不能直接把它复活。
                SuspendIfPastConfirmedDeadlineLocked();
                if (_time.GetElapsedTime(attemptStartedAt, attemptFinishedAt) >= _localConfirmedTtl)
                {
                    if (_state == LeaseState.Active)
                        _state = LeaseState.Suspended;
                    return ConfirmationResult.RetryFresh;
                }

                if (recoveryAttempt)
                {
                    _confirmedStartedAt = attemptStartedAt;
                    _state = LeaseState.Active;
                    return ConfirmationResult.Confirmed;
                }

                if (_state == LeaseState.Active)
                {
                    _confirmedStartedAt = attemptStartedAt;
                    return ConfirmationResult.Confirmed;
                }

                return ConfirmationResult.RetryFresh;
            }
        }
        finally
        {
            _confirmationGate.Release();
        }
    }

    private bool IsTerminal()
    {
        lock (_stateSync)
            return _state is LeaseState.Lost or LeaseState.Disposed;
    }

    private void SuspendIfPastConfirmedDeadline()
    {
        lock (_stateSync)
            SuspendIfPastConfirmedDeadlineLocked();
    }

    private void SuspendIfPastConfirmedDeadlineLocked()
    {
        if (_state == LeaseState.Active
            && _time.GetElapsedTime(_confirmedStartedAt) >= _localConfirmedTtl)
        {
            _state = LeaseState.Suspended;
        }
    }

    /// <summary>读取数据库自己的当前时间，领取、续租和过期比较共用同一时钟。</summary>
    internal static DateTime DatabaseNow(ISqlSugarClient db)
    {
        var value = db.Ado.GetScalar("SELECT CURRENT_TIMESTAMP");
        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.DateTime,
            null or DBNull => throw new InvalidOperationException("数据库未返回当前时间，无法维护 WorkerId 租约。"),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_stateSync)
        {
            if (_state == LeaseState.Disposed)
                return;
            _state = LeaseState.Disposed;
        }

        try
        {
            _cts.Cancel();
            try
            {
                _renewTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
            catch (Exception ex)
            {
                SafeLogWarning(
                    "WorkerId {WorkerId} 数据库租约续租循环停止失败({ErrorType})。",
                    _workerId,
                    ex.GetType().Name);
            }

            // 等后台循环退出后再取得同一闸门，确保没有确认请求仍在使用这个独立客户端。
            _confirmationGate.Wait();
            try
            {
                try
                {
                    _db.Deleteable<SysWorkerLease>()
                        .Where(l => l.WorkerId == _workerId && l.NodeName == _nodeName)
                        .ExecuteCommand();
                }
                catch (Exception ex)
                {
                    SafeLogWarning(
                        "WorkerId {WorkerId} 数据库租约释放失败({ErrorType})，将等待数据库到期。",
                        _workerId,
                        ex.GetType().Name);
                }

                if (_ownsClient)
                {
                    try
                    {
                        _db.Dispose();
                    }
                    catch (Exception ex)
                    {
                        SafeLogWarning(
                            "WorkerId {WorkerId} 数据库租约连接释放失败({ErrorType})。",
                            _workerId,
                            ex.GetType().Name);
                    }
                }
            }
            finally
            {
                _confirmationGate.Release();
            }
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private void SafeLogWarning(string message, params object?[] args)
    {
        try
        {
            _logger?.LogWarning(message, args);
        }
        catch
        {
            // 停机时日志提供者可能已释放；日志失败不能破坏租约释放。
        }
    }
}
