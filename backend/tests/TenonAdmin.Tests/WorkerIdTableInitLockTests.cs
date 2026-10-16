using System.Diagnostics;
using Microsoft.Data.Sqlite;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// #62:建表阶段(InitTables)遇到 SQLite 排他锁时,应被识别为锁冲突,按 #52 的有界次数重试;
/// 重试耗尽后快速失败并保留锁冲突信息,而不是挂起数分钟后抛「需要系统表权限」。
/// 仅 SQLite 腿有意义(其他库无文件级排他锁),非 SQLite 腿直接返回。
/// </summary>
public sealed class WorkerIdTableInitLockTests : IDisposable
{
    private readonly string _identity = $"wid-initlock-{Guid.NewGuid():N}";
    private readonly string _dbFile;
    private readonly string _lockDir;
    private readonly string _conn;

    public WorkerIdTableInitLockTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_identity}.db");
        _lockDir = Path.Combine(Path.GetTempPath(), _identity);
        Directory.CreateDirectory(_lockDir);
        _conn = $"Data Source={_dbFile};Default Timeout=1;Pooling=False";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbFile); } catch { }
        try { Directory.Delete(_lockDir, true); } catch { }
    }

    private SqliteConnection HoldExclusive()
    {
        var holder = new SqliteConnection(_conn);
        holder.Open();
        using var cmd = holder.CreateCommand();
        cmd.CommandText = "CREATE TABLE qa_hold(x int); BEGIN EXCLUSIVE; INSERT INTO qa_hold VALUES(1);";
        cmd.ExecuteNonQuery();
        return holder;
    }

    private WorkerIdAssignment Claim() =>
        new DbWorkerIdSlotClaimer(new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = _conn }, new AdminJobsOptions())
            .Claim(new AdminIdOptions { WorkerIdLockDir = _lockDir });

    [Fact]
    public async Task Table_init_lock_conflict_fails_fast_as_lock_conflict_after_bounded_retries()
    {
        if (TestDb.DbType != "Sqlite") return;
        using var holder = HoldExclusive();

        var sw = Stopwatch.StartNew();
        var ex = await Record.ExceptionAsync(() => Task.Run(Claim));
        sw.Stop();

        Assert.NotNull(ex);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"建表锁冲突应快速失败,实际 {sw.Elapsed}");
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(ex!), $"应识别为锁冲突,实际 {ex!.GetType().Name}: {ex.Message}");
        Assert.Contains("锁", ex.Message);
    }

    [Fact]
    public async Task Table_init_lock_released_during_retry_then_claim_succeeds()
    {
        if (TestDb.DbType != "Sqlite") return;
        var holder = HoldExclusive();
        // 首次尝试会在 busy timeout(1s)后失败;在重试窗口内释放锁,后续重试应成功。
        // 用独立线程计时释放,避免全量并行跑测时线程池饥饿把释放推迟到重试窗口(约 3 × 1s)之外。
        await ClaimWhileReleasing(
            Claim,
            release: () =>
            {
                Thread.Sleep(1500);
                using var cmd = holder.CreateCommand();
                cmd.CommandText = "COMMIT;";
                cmd.ExecuteNonQuery();
            },
            dispose: holder.Dispose,
            verify: a => Assert.InRange(a.WorkerId, 0, SnowflakeIdGenerator.MaxWorkerId));
    }

    /// <summary>
    /// 在独立线程执行 release 后一定执行 dispose,同时在线程池上执行 claim。
    /// 保证:无论 claim 成败都 Join;release / dispose / claim / verify 的异常都回到调用线程;
    /// claim 成功后无论 verify 是否失败,领取结果都会 Dispose。
    /// 单个错误按原异常(保留堆栈)重抛,多个错误以 AggregateException 一并呈现。
    /// </summary>
    internal static async Task ClaimWhileReleasing<T>(Func<T> claim, Action release, Action dispose, Action<T> verify)
        where T : class, IDisposable
    {
        Exception? releaseError = null, disposeError = null;
        var thread = new Thread(() =>
        {
            try { release(); }
            catch (Exception e) { releaseError = e; }
            finally
            {
                try { dispose(); }
                catch (Exception e) { disposeError = e; }
            }
        }) { IsBackground = true };
        thread.Start();

        T? result = null;
        Exception? claimError = null;
        try { result = await Task.Run(claim); }
        catch (Exception e) { claimError = e; }
        finally { thread.Join(); }

        var errors = new List<Exception>();
        if (claimError is not null) errors.Add(claimError);
        if (releaseError is not null) errors.Add(releaseError);
        if (disposeError is not null) errors.Add(disposeError);
        try
        {
            if (errors.Count == 0) verify(result!);
        }
        catch (Exception e) { errors.Add(e); }
        finally
        {
            if (result is not null)
            {
                try { result.Dispose(); }
                catch (Exception e) { errors.Add(e); }
            }
        }

        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Claim/释放锁线程出现多个错误", errors);
    }

    // ---------- ClaimWhileReleasing 清理与异常回传的确定性回归(review #69 round 9,与数据库无关) ----------

    private sealed class FakeAssignment : IDisposable
    {
        public int DisposeCount;
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    private static FakeAssignment ThrowClaim() => throw new InvalidOperationException("claim-fail");

    [Fact]
    public async Task Helper_claim_and_release_both_fail_reports_both()
    {
        var disposed = 0;
        var ex = await Assert.ThrowsAsync<AggregateException>(() => ClaimWhileReleasing<FakeAssignment>(
            ThrowClaim, () => throw new IOException("release-fail"), () => disposed++, _ => { }));
        Assert.Collection(ex.InnerExceptions,
            e => Assert.Equal("claim-fail", Assert.IsType<InvalidOperationException>(e).Message),
            e => Assert.Equal("release-fail", Assert.IsType<IOException>(e).Message));
        Assert.Equal(1, disposed); // release 失败后 dispose 仍执行,且在返回前已 Join
    }

    [Fact]
    public async Task Helper_dispose_failure_is_reported_and_assignment_released()
    {
        var a = new FakeAssignment();
        var disposeEx = new ObjectDisposedException("holder-dispose-fail");
        var ex = await Assert.ThrowsAsync<ObjectDisposedException>(() => ClaimWhileReleasing(
            () => a, () => { }, () => throw disposeEx, _ => { }));
        Assert.Same(disposeEx, ex);
        Assert.Equal(1, a.DisposeCount);
    }

    [Fact]
    public async Task Helper_release_and_dispose_both_fail_reports_both()
    {
        var a = new FakeAssignment();
        var ex = await Assert.ThrowsAsync<AggregateException>(() => ClaimWhileReleasing(
            () => a, () => throw new IOException("release-fail"), () => throw new ObjectDisposedException("dispose-fail"), _ => { }));
        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.IsType<IOException>(ex.InnerExceptions[0]);
        Assert.IsType<ObjectDisposedException>(ex.InnerExceptions[1]);
        Assert.Equal(1, a.DisposeCount);
    }

    [Fact]
    public async Task Helper_single_claim_error_keeps_original_exception_and_stack()
    {
        var disposed = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ClaimWhileReleasing<FakeAssignment>(
            ThrowClaim, () => { }, () => disposed++, _ => { }));
        Assert.Equal("claim-fail", ex.Message);
        Assert.Contains(nameof(ThrowClaim), ex.StackTrace);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task Helper_verify_failure_after_successful_claim_still_releases_assignment()
    {
        var a = new FakeAssignment();
        var verifyEx = new Xunit.Sdk.XunitException("verify-fail");
        var ex = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => ClaimWhileReleasing(
            () => a, () => { }, () => { }, _ => throw verifyEx));
        Assert.Same(verifyEx, ex);
        Assert.Equal(1, a.DisposeCount);
    }

    // ---------- 只有 SQLite 改抛友好异常;其他库原样传播(review #69) ----------

    [Fact]
    public void Friendly_wrap_applies_to_sqlite_lock_conflicts_only()
    {
        Assert.True(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(global::SqlSugar.DbType.Sqlite, new SqliteException("busy", 5)));

        var mysqlDeadlock = WorkerIdSlotErrorClassificationTests.MySqlError(1213, "40001", "Deadlock found when trying to get lock");
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(mysqlDeadlock)); // 仍按锁冲突重试
        foreach (var dbType in new[] { global::SqlSugar.DbType.MySql, global::SqlSugar.DbType.PostgreSQL, global::SqlSugar.DbType.SqlServer })
        {
            Assert.False(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(dbType, mysqlDeadlock));
            Assert.False(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(dbType, new SqliteException("busy", 5)));
        }

        // F01-C:取消包住锁错误时不算锁冲突,也不包装。
        var cancel = new OperationCanceledException("QA cancelled", new SqliteException("busy", 5), new CancellationToken(true));
        Assert.False(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(global::SqlSugar.DbType.Sqlite, cancel));
    }
}
