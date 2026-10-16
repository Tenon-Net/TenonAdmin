using System.Diagnostics;
using Npgsql;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// #70:PostgreSQL 建表阶段遇到锁超时(SQLSTATE 55P03)时,应按错误码识别为锁冲突、有界重试 3 次后
/// 原样抛出 <see cref="PostgresException"/>(修复前 SqlSugar 改写为无 InnerException 的 SqlSugarException,不重试)。
/// 构造方式:删列制造结构漂移 → Claim 必然 ALTER TABLE ADD;持锁连接 BEGIN; LOCK TABLE ... ACCESS EXCLUSIVE 不提交;
/// Claim 连接串用 Options=-c lock_timeout=1000 只对自身会话生效(不改全局)。仅 PostgreSQL 腿运行。
/// </summary>
public sealed class WorkerIdTableInitLockPostgreSqlTests : IDisposable
{
    private readonly string _identity = $"wid-pglk-{Guid.NewGuid():N}"[..20];
    private readonly string _dbFile;
    private readonly string _lockDir;

    public WorkerIdTableInitLockPostgreSqlTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_identity}.db");
        _lockDir = Path.Combine(Path.GetTempPath(), _identity);
        Directory.CreateDirectory(_lockDir);
    }

    public void Dispose()
    {
        if (TestDb.UsePostgreSql) TestDb.Cleanup(_identity, _dbFile);
        try { Directory.Delete(_lockDir, true); } catch { }
    }

    private sealed class Outcome(Exception? error, WorkerIdAssignment? assignment) : IDisposable
    {
        public Exception? Error { get; } = error;
        public void Dispose() => assignment?.Dispose();
    }

    [Fact]
    public async Task Lock_timeout_during_table_init_is_retried_three_times_then_original_postgres_error_propagates()
    {
        if (!TestDb.UsePostgreSql) return;

        var cs = TestDb.ConnectionString(_identity, _dbFile).TrimEnd(';');
        var claimCs = cs + ";Pooling=false;Options=-c lock_timeout=1000;";
        WorkerIdAssignment Claim(string connectionString) =>
            new DbWorkerIdSlotClaimer(new AdminDatabaseOptions { DbType = "PostgreSQL", ConnectionString = connectionString }, new AdminJobsOptions())
                .Claim(new AdminIdOptions { WorkerIdLockDir = _lockDir });

        using (Claim(cs + ";Pooling=false;")) { } // 建表
        var column = DropMachineNameColumn(cs);

        await using var holder = new NpgsqlConnection(cs + ";Pooling=false;");
        holder.Open();
        await using (var cmd = new NpgsqlCommand("BEGIN; LOCK TABLE sys_worker_lease IN ACCESS EXCLUSIVE MODE;", holder))
            cmd.ExecuteNonQuery();
        var holderPid = holder.ProcessID;

        var waits = new List<long>();
        var done = 0;
        var sw = Stopwatch.StartNew();
        await WorkerIdTableInitLockTests.ClaimWhileReleasing(
            claim: () =>
            {
                try { return new Outcome(null, Claim(claimCs)); }
                catch (Exception e) { return new Outcome(e, null); }
                finally { sw.Stop(); Volatile.Write(ref done, 1); }
            },
            release: () =>
            {
                // 监视:每 50ms 采样 pg_stat_activity,某后端由「不在等锁」变为「在等 Lock」记一段。
                using var m = new NpgsqlConnection(cs + ";Pooling=false;");
                m.Open();
                var prev = new HashSet<int>();
                while (Volatile.Read(ref done) == 0)
                {
                    var cur = new HashSet<int>();
                    using (var c = new NpgsqlCommand(
                               "SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> @h", m)
                           { Parameters = { new("h", holderPid) } })
                    using (var r = c.ExecuteReader())
                        while (r.Read())
                            cur.Add(r.GetInt32(0));
                    foreach (var pid in cur)
                        if (!prev.Contains(pid)) waits.Add(sw.ElapsedMilliseconds);
                    prev = cur;
                    Thread.Sleep(50);
                }
            },
            dispose: () =>
            {
                using var c = new NpgsqlCommand("ROLLBACK", holder);
                c.ExecuteNonQuery();
            },
            verify: o =>
            {
                var ex = o.Error;
                Assert.NotNull(ex);
                var pg = Assert.IsType<PostgresException>(ex); // 原始驱动异常
                Assert.Equal("55P03", pg.SqlState);
                Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(ex!));
                Assert.False(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(global::SqlSugar.DbType.PostgreSQL, ex!));
                Assert.Equal(3, waits.Count); // MaxLockConflictAttempts
                Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(2.5), $"3 次 × 1s 锁等待,实际 {sw.Elapsed}");
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"应有界失败,实际 {sw.Elapsed}");
            });

        await using var check = new NpgsqlConnection(cs + ";Pooling=false;");
        check.Open();
        await using var q = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'sys_worker_lease' AND column_name = @c", check)
        { Parameters = { new("c", column) } };
        Assert.Equal(0L, Convert.ToInt64(q.ExecuteScalar()));
    }

    /// <summary>删掉 MachineName 列(SqlSugar 在 PG 上可能转小写,按实际列名删),返回实际列名。</summary>
    private static string DropMachineNameColumn(string cs)
    {
        using var c = new NpgsqlConnection(cs + ";Pooling=false;");
        c.Open();
        string column;
        using (var q = new NpgsqlCommand(
                   "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'sys_worker_lease' AND lower(column_name) = 'machinename'", c))
            column = (string)q.ExecuteScalar()!;
        using (var d = new NpgsqlCommand($"ALTER TABLE sys_worker_lease DROP COLUMN \"{column}\"", c))
            d.ExecuteNonQuery();
        return column;
    }
}
