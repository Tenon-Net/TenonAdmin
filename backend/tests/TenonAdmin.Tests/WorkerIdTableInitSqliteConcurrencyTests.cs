using System.Reflection;
using System.Runtime.ExceptionServices;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>SqlSugar 的 CodeFirst 锁是进程级的,本用例独占运行以固定两个调用方的锁获取顺序。</summary>
[Collection(WorkerIdTableInitSqliteConcurrencyCollection.Name)]
public sealed class WorkerIdTableInitSqliteConcurrencyTests
{
    [Fact]
    public void Claim_and_bare_init_tables_do_not_invert_sqlite_write_and_codefirst_locks()
    {
        // 始终使用独立 SQLite 库;非 SQLite 矩阵也执行这条 SQLite 锁序回归。
        var root = Path.Combine(Path.GetTempPath(), $"wid-init-order-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "case.db")};Default Timeout=1;Pooling=False";
        SqlSugarClient OpenClient()
        {
            var client = new SqlSugarClient(new ConnectionConfig
            {
                DbType = global::SqlSugar.DbType.Sqlite,
                ConnectionString = connectionString,
                IsAutoCloseConnection = true,
            });
            client.Ado.CommandTimeOut = 1;
            return client;
        }

        WorkerIdAssignment? assignment = null;
        Exception? claimError = null;
        var claimThread = new Thread(() =>
        {
            try
            {
                assignment = new DbWorkerIdSlotClaimer(new AdminDatabaseOptions
                {
                    DbType = "Sqlite", ConnectionString = connectionString,
                }).Claim(new AdminIdOptions { WorkerIdLockDir = Path.Combine(root, "locks") });
            }
            catch (Exception ex) { claimError = ex; }
        }) { IsBackground = true };
        var started = false;
        var errors = new List<Exception>();
        try
        {
            using (var control = OpenClient())
                control.CodeFirst.InitTables<SysWorkerLease>();

            // 固定在裸 InitTables 已拿内部锁、尚未 BeginTran 的调度边界。
            // 反射只用于调度,实际执行的仍是 public Claim 和驱动的真实 InitTables。
            // SqlSugar 升级后若内部锁字段变化,必须明确失败,不能退化成未固定调度的假通过。
            var provider = typeof(SqlSugarClient).Assembly.GetType("SqlSugar.CodeFirstProvider", throwOnError: true)!;
            var field = provider.GetField("LockObject", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var codeFirstGate = field.GetValue(null)!;
            lock (codeFirstGate)
            {
                claimThread.Start();
                started = true;
                Assert.True(SpinWait.SpinUntil(
                    () => (claimThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(10)), "Claim 应在 CodeFirst 锁处等待,不能提前失败或一直未开始执行");

                // 当前线程重入内部锁。Claim 此时必须尚未持有 SQLite 写锁,否则这里会超时。
                using var bare = OpenClient();
                bare.CodeFirst.InitTables<SysWorkerLease>();
            }
        }
        catch (Exception ex) { errors.Add(ex); }
        finally
        {
            // 离开 lock 后一定 Join,再读取和释放 assignment,不遗失后台线程稍后返回的租约。
            if (started) claimThread.Join();
            if (claimError is not null) errors.Add(claimError);
            try
            {
                if (errors.Count == 0)
                {
                    Assert.NotNull(assignment);
                    Assert.InRange(assignment.WorkerId, 0, SnowflakeIdGenerator.MaxWorkerId);
                }
            }
            catch (Exception ex) { errors.Add(ex); }
            finally
            {
                try { assignment?.Dispose(); }
                catch (Exception ex) { errors.Add(ex); }
                // 所有连接均 Pooling=False,无需清理其他测试的连接池。
                try { Directory.Delete(root, recursive: true); }
                catch (Exception ex) { errors.Add(ex); }
            }
        }

        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("SQLite 初始化与 Claim/清理出现多个错误", errors);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerIdTableInitSqliteConcurrencyCollection
{
    public const string Name = "WorkerIdTableInitSqliteConcurrency";
}
