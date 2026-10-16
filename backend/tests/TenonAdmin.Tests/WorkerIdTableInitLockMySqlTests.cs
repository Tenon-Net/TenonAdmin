using System.Diagnostics;
using MySqlConnector;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// #70:MySQL 建表阶段(InitTables 改表)遇到元数据锁(MDL)时,应按驱动错误码 1205 识别为锁冲突、
/// 有界重试 3 次后原样抛出 <see cref="MySqlException"/>(不包装成中文异常,也不再是丢了错误码的 SqlSugarException)。
/// 构造方式:删掉 MachineName 列制造结构漂移 → Claim 必然 ALTER ADD COLUMN(需排他 MDL);
/// 持锁连接开事务 SELECT 不提交(持共享 MDL);GLOBAL lock_wait_timeout=1 让每次 ALTER 约 1 秒超时。
/// 修改 GLOBAL 变量,故放进独占(不并行)集合并在 finally 恢复。仅 MySQL 腿运行。
/// </summary>
[Collection(WorkerIdTableInitLockMySqlCollection.Name)]
public sealed class WorkerIdTableInitLockMySqlTests : IDisposable
{
    private readonly string _identity = $"wid-mdl-{Guid.NewGuid():N}"[..20];
    private readonly string _dbFile;
    private readonly string _lockDir;

    public WorkerIdTableInitLockMySqlTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_identity}.db");
        _lockDir = Path.Combine(Path.GetTempPath(), _identity);
        Directory.CreateDirectory(_lockDir);
    }

    public void Dispose()
    {
        if (TestDb.UseMySql) TestDb.Cleanup(_identity, _dbFile);
        try { Directory.Delete(_lockDir, true); } catch { }
    }

    private sealed class Outcome(Exception? error, WorkerIdAssignment? assignment) : IDisposable
    {
        public Exception? Error { get; } = error;
        public void Dispose() => assignment?.Dispose();
    }

    [Fact]
    public async Task Mdl_lock_during_table_init_is_retried_three_times_then_original_mysql_error_propagates()
    {
        if (!TestDb.UseMySql) return;

        // Pooling=false:每次重试都是新会话,继承本测试设置的 GLOBAL lock_wait_timeout。
        var cs = TestDb.ConnectionString(_identity, _dbFile).TrimEnd(';') + ";Pooling=false;";
        WorkerIdAssignment Claim() =>
            new DbWorkerIdSlotClaimer(new AdminDatabaseOptions { DbType = "MySql", ConnectionString = cs }, new AdminJobsOptions())
                .Claim(new AdminIdOptions { WorkerIdLockDir = _lockDir });

        using (Claim()) { } // 建表
        using (var c = new MySqlConnection(cs))
        {
            c.Open();
            Exec(c, "ALTER TABLE sys_worker_lease DROP COLUMN MachineName");
        }
        var dbName = new MySqlConnectionStringBuilder(cs).Database;

        using var admin = new MySqlConnection(cs);
        admin.Open();
        var original = Convert.ToInt64(Scalar(admin, "SELECT @@GLOBAL.lock_wait_timeout"));
        var holder = new MySqlConnection(cs);
        holder.Open();
        Exec(holder, "START TRANSACTION; SELECT * FROM sys_worker_lease LIMIT 1;");

        var waits = new List<long>(); // 每段 MDL 等待开始时刻(ms)
        var done = 0;
        var sw = new Stopwatch();
        try
        {
            Exec(admin, "SET GLOBAL lock_wait_timeout = 1");
            sw.Start();
            await WorkerIdTableInitLockTests.ClaimWhileReleasing(
                claim: () =>
                {
                    try { return new Outcome(null, Claim()); }
                    catch (Exception e) { return new Outcome(e, null); }
                    finally { sw.Stop(); Volatile.Write(ref done, 1); }
                },
                release: () =>
                {
                    // 监视:每 50ms 采样 PROCESSLIST,某会话由「不在等」变为「在等 MDL」记一段。
                    using var m = new MySqlConnection(cs);
                    m.Open();
                    var prev = new HashSet<long>();
                    while (Volatile.Read(ref done) == 0)
                    {
                        var cur = new HashSet<long>();
                        using (var r = new MySqlCommand(
                                   "SELECT ID FROM information_schema.PROCESSLIST WHERE DB = @db " +
                                   "AND STATE = 'Waiting for table metadata lock' AND INFO LIKE 'ALTER TABLE%sys_worker_lease%'", m)
                               { Parameters = { new("@db", dbName) } }.ExecuteReader())
                            while (r.Read())
                                cur.Add(r.GetInt64(0));
                        foreach (var id in cur)
                            if (!prev.Contains(id)) waits.Add(sw.ElapsedMilliseconds);
                        prev = cur;
                        Thread.Sleep(50);
                    }
                },
                dispose: () =>
                {
                    try { Exec(holder, "ROLLBACK"); }
                    finally { holder.Dispose(); }
                },
                verify: o =>
                {
                    var ex = o.Error;
                    Assert.NotNull(ex);
                    var my = Assert.IsType<MySqlException>(ex); // 原始驱动异常,不是 SqlSugarException / 中文 InvalidOperationException
                    Assert.Equal(1205, my.Number);
                    Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(ex!));
                    Assert.False(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(global::SqlSugar.DbType.MySql, ex!));
                    Assert.Equal(3, waits.Count); // MaxLockConflictAttempts
                    Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(2.5), $"3 次 × 1s 锁等待,实际 {sw.Elapsed}");
                    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"应有界失败,实际 {sw.Elapsed}");
                });
        }
        finally
        {
            Exec(admin, $"SET GLOBAL lock_wait_timeout = {original}");
            Assert.Equal(original, Convert.ToInt64(Scalar(admin, "SELECT @@GLOBAL.lock_wait_timeout")));
        }

        using var check = new MySqlConnection(cs);
        check.Open();
        Assert.Equal(0L, Convert.ToInt64(Scalar(check,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'sys_worker_lease' AND COLUMN_NAME = 'MachineName'")));
    }

    private static void Exec(MySqlConnection c, string sql)
    {
        using var cmd = new MySqlCommand(sql, c);
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(MySqlConnection c, string sql)
    {
        using var cmd = new MySqlCommand(sql, c);
        return cmd.ExecuteScalar();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerIdTableInitLockMySqlCollection
{
    public const string Name = "WorkerIdTableInitLockMySql";
}
