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
        Exception? releaseError = null;
        // 首次尝试会在 busy timeout(1s)后失败;在重试窗口内释放锁,后续重试应成功。
        // 用独立线程计时释放,避免全量并行跑测时线程池饥饿把释放推迟到重试窗口(约 3 × 1s)之外。
        // 线程内的异常不吞掉:捕获后回到测试线程报告;holder 一定释放;无论 Claim 成功与否都 Join。
        var release = new Thread(() =>
        {
            try
            {
                Thread.Sleep(1500);
                using var cmd = holder.CreateCommand();
                cmd.CommandText = "COMMIT;";
                cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                releaseError = e;
            }
            finally
            {
                holder.Dispose();
            }
        }) { IsBackground = true };
        release.Start();

        WorkerIdAssignment? assignment = null;
        try
        {
            assignment = await Task.Run(Claim);
        }
        finally
        {
            release.Join();
        }

        using (assignment)
        {
            if (releaseError is not null)
                throw new Xunit.Sdk.XunitException($"释放锁线程失败:{releaseError}");
            Assert.InRange(assignment.WorkerId, 0, SnowflakeIdGenerator.MaxWorkerId);
        }
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
