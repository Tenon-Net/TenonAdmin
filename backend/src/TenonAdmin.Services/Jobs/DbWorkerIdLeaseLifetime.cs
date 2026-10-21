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
    private const int Active = 0;
    private const int Lost = 1;
    private const int Disposed = 2;

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
    private readonly Task _renewTask;

    private long _confirmedStartedAt;
    private int _state;

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
        ExpireIfPastConfirmedDeadline();
        var state = Volatile.Read(ref _state);
        if (state == Disposed)
            throw new InvalidOperationException("WorkerId 数据库租约已释放，拒绝继续发号。");
        if (state != Active)
            throw new InvalidOperationException("WorkerId 数据库租约已丢失或过期，拒绝继续发号。");
    }

    private async Task RenewLoopAsync()
    {
        while (Volatile.Read(ref _state) == Active)
        {
            try
            {
                await Task.Delay(_renewInterval, _time, _cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }

            if (ExpireIfPastConfirmedDeadline())
                return;

            try
            {
                // 成功确认的本地起点取在 DB 时钟查询前；查询和更新耗时都会消耗本轮 TTL，
                // 不能到响应返回时才开始计算，否则卡住的连接会虚增有效期。
                var attemptStartedAt = _time.GetTimestamp();
                var databaseNow = DatabaseNow(_db);
                var expiresAt = databaseNow + _leaseTtl;
                var rows = _db.Updateable<SysWorkerLease>()
                    .SetColumns(l => new SysWorkerLease { LeaseExpiresAt = expiresAt })
                    .Where(l => l.WorkerId == _workerId
                        && l.NodeName == _nodeName
                        && l.LeaseExpiresAt > databaseNow)
                    .ExecuteCommand();

                if (rows == 0)
                {
                    MarkLost();
                    SafeLogWarning("WorkerId {WorkerId} 的数据库租约已被接管或过期，停止续租。", _workerId);
                    return;
                }

                // 一旦旧的确认期限已经过去，即使迟到的 UPDATE 成功也不能恢复为有效状态。
                if (ExpireIfPastConfirmedDeadline())
                    return;

                if (Volatile.Read(ref _state) == Active)
                    Interlocked.Exchange(ref _confirmedStartedAt, attemptStartedAt);
            }
            catch (Exception ex)
            {
                // 短暂故障只能容忍到最后一次确认的本地期限；日志只写异常类型，避免泄漏连接信息。
                SafeLogWarning(
                    "WorkerId {WorkerId} 数据库租约续租失败({ErrorType})，将在确认期限内重试。",
                    _workerId,
                    ex.GetType().Name);
                if (ExpireIfPastConfirmedDeadline())
                    return;
            }
        }
    }

    private bool ExpireIfPastConfirmedDeadline()
    {
        if (Volatile.Read(ref _state) != Active)
            return true;

        var confirmed = Interlocked.Read(ref _confirmedStartedAt);
        if (_time.GetElapsedTime(confirmed) < _localConfirmedTtl)
            return false;

        MarkLost();
        return true;
    }

    private void MarkLost() => Interlocked.CompareExchange(ref _state, Lost, Active);

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
        if (Interlocked.Exchange(ref _state, Disposed) == Disposed)
            return;

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
        }
        finally
        {
            _cts.Dispose();
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
