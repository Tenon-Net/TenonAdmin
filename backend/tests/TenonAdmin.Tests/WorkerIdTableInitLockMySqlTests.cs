using System.Diagnostics;
using MySqlConnector;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// #70:MySQL 建表阶段(InitTables 改表)遇到元数据锁(MDL)时,应按驱动错误码 1205 识别为锁冲突、
/// 有界重试 3 次后原样抛出 <see cref="MySqlException"/>(不包装成中文异常,也不再是丢了错误码的 SqlSugarException)。
/// 构造方式:删掉 MachineName 列制造结构漂移 → Claim 必然 ALTER ADD COLUMN(需排他 MDL);
/// 持锁连接开事务 SELECT 不提交(持共享 MDL);Claim 独占连接池的 SESSION lock_wait_timeout=1
/// 让每次 ALTER 约 1 秒超时,不修改实例全局设置或其它会话。仅 MySQL 腿运行。
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

        var baseCs = TestDb.ConnectionString(_identity, _dbFile);
        var cs = new MySqlConnectionStringBuilder(baseCs) { Pooling = false }.ConnectionString;
        // SqlSugar 每次重试会自动开关连接。唯一连接池固定为一个不重置的物理会话,
        // 预热时设置 SESSION 超时,使三次重试只影响 Claim,不修改实例全局变量。
        var claimCs = new MySqlConnectionStringBuilder(baseCs)
        {
            Pooling = true,
            ConnectionReset = false,
            MaximumPoolSize = 1,
            ApplicationName = $"TenonWorkerIdMdl-{_identity}"
        }.ConnectionString;
        WorkerIdAssignment Claim() =>
            new DbWorkerIdSlotClaimer(new AdminDatabaseOptions { DbType = "MySql", ConnectionString = claimCs }, new AdminJobsOptions())
                .Claim(new AdminIdOptions { WorkerIdLockDir = _lockDir });

        // 必须让 SqlSugar 构造连接:MySqlProvider 会规范化连接串,直接 new MySqlConnection(claimCs)
        // 可能落入另一个池,无法给 Claim 实际使用的会话设置 SESSION 变量。
        using var claimPoolClient = new global::SqlSugar.SqlSugarClient(new global::SqlSugar.ConnectionConfig
        {
            ConfigId = "TenonAdmin.WorkerIdClaim",
            DbType = global::SqlSugar.DbType.MySql,
            ConnectionString = claimCs,
            IsAutoCloseConnection = true,
            MoreSettings = new global::SqlSugar.ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        });
        var claimPoolConnection = Assert.IsType<MySqlConnection>(claimPoolClient.Ado.Connection);

        try
        {
            using (Claim()) { } // 建表并创建 Claim 独占池
            using (var c = new MySqlConnection(cs))
            {
                c.Open();
                Exec(c, "ALTER TABLE sys_worker_lease DROP COLUMN MachineName");
            }
            var dbName = new MySqlConnectionStringBuilder(cs).Database;

            using var admin = new MySqlConnection(cs);
            admin.Open();
            var original = Convert.ToInt64(Scalar(admin, "SELECT @@GLOBAL.lock_wait_timeout"));
            Assert.Equal("YES", Convert.ToString(Scalar(admin,
                "SELECT ENABLED FROM performance_schema.setup_consumers WHERE NAME = 'events_statements_current'")));
            Assert.Equal("YES", Convert.ToString(Scalar(admin,
                "SELECT ENABLED FROM performance_schema.setup_instruments WHERE NAME = 'statement/sql/alter_table'")));
            _ = Convert.ToInt64(Scalar(admin, "SELECT COUNT(*) FROM performance_schema.events_statements_current"));

            long claimConnectionId;
            claimPoolConnection.Open();
            try
            {
                Exec(claimPoolConnection, "SET SESSION lock_wait_timeout = 1");
                Assert.Equal(1L, Convert.ToInt64(Scalar(claimPoolConnection, "SELECT @@SESSION.lock_wait_timeout")));
                claimConnectionId = Convert.ToInt64(Scalar(claimPoolConnection, "SELECT CONNECTION_ID()"));
            }
            finally { claimPoolConnection.Close(); } // 归还唯一 Claim 池,供 SqlSugar 的三次自动 Open 复用

            using var holder = new MySqlConnection(cs);
            holder.Open();
            Exec(holder, "START TRANSACTION; SELECT * FROM sys_worker_lease LIMIT 1;");

            var waits = new List<long>(); // 每段 MDL 等待开始时刻(ms)
            var waitingSessions = new List<long>();
            var done = 0;
            var sw = new Stopwatch();
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
                    // 同一物理会话连续重试时 PROCESSLIST_ID 不变;每次 ALTER 的 EVENT_ID 不同。
                    // 用 (THREAD_ID, EVENT_ID) 去重,即使两次重试间的空窗未被采到也不会漏段。
                    using var m = new MySqlConnection(cs);
                    m.Open();
                    var seen = new HashSet<(ulong ThreadId, ulong EventId)>();
                    while (Volatile.Read(ref done) == 0)
                    {
                        if (sw.Elapsed >= TimeSpan.FromSeconds(30))
                            throw new TimeoutException("30 秒内 Claim 未结束;将释放持锁事务并使测试失败。");
                        using (var r = new MySqlCommand(
                                   "SELECT e.THREAD_ID, e.EVENT_ID, t.PROCESSLIST_ID " +
                                   "FROM performance_schema.events_statements_current e " +
                                   "JOIN performance_schema.threads t ON t.THREAD_ID = e.THREAD_ID " +
                                   "JOIN information_schema.PROCESSLIST p ON p.ID = t.PROCESSLIST_ID " +
                                   "WHERE p.DB = @db AND p.STATE = 'Waiting for table metadata lock' " +
                                   "AND e.EVENT_NAME = 'statement/sql/alter_table' " +
                                   "AND e.SQL_TEXT LIKE 'ALTER TABLE%sys_worker_lease%'", m)
                               { Parameters = { new("@db", dbName) } }.ExecuteReader())
                            while (r.Read())
                            {
                                var attempt = (r.GetUInt64(0), r.GetUInt64(1));
                                if (!seen.Add(attempt)) continue;
                                waits.Add(sw.ElapsedMilliseconds);
                                waitingSessions.Add(r.GetInt64(2));
                            }
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
                    // 锁超时只影响 Claim 独占池中的会话;全局设置及无关的新连接保持原值。
                    Assert.Equal(original, Convert.ToInt64(Scalar(admin, "SELECT @@GLOBAL.lock_wait_timeout")));
                    using var unrelated = new MySqlConnection(cs);
                    unrelated.Open();
                    Assert.Equal(original, Convert.ToInt64(Scalar(unrelated, "SELECT @@SESSION.lock_wait_timeout")));
                    var ex = o.Error;
                    Assert.NotNull(ex);
                    var my = Assert.IsType<MySqlException>(ex); // 原始驱动异常,不是 SqlSugarException / 中文 InvalidOperationException
                    Assert.Equal(1205, my.Number);
                    Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(ex!));
                    Assert.False(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(global::SqlSugar.DbType.MySql, ex!));
                    Assert.Equal(3, waits.Count); // MaxLockConflictAttempts
                    Assert.Equal(new[] { claimConnectionId, claimConnectionId, claimConnectionId }, waitingSessions);
                    Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(2.5), $"3 次 × 1s 锁等待,实际 {sw.Elapsed}");
                    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"应有界失败,实际 {sw.Elapsed}");
                });

            using var check = new MySqlConnection(cs);
            check.Open();
            Assert.Equal(0L, Convert.ToInt64(Scalar(check,
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME = 'sys_worker_lease' AND COLUMN_NAME = 'MachineName'")));
        }
        finally
        {
            claimPoolConnection.Close();
            MySqlConnection.ClearPool(claimPoolConnection);
        }
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
