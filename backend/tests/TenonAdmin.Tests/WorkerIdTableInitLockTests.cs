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
        var release = new Thread(() =>
        {
            Thread.Sleep(1500);
            using (var cmd = holder.CreateCommand()) { cmd.CommandText = "COMMIT;"; cmd.ExecuteNonQuery(); }
            holder.Dispose();
        }) { IsBackground = true };
        release.Start();
        using var assignment = await Task.Run(Claim);
        release.Join();
        Assert.InRange(assignment.WorkerId, 0, SnowflakeIdGenerator.MaxWorkerId);
    }
}
