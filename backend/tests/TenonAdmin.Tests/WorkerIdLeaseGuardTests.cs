using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// QA27: WorkerId 数据库租约守卫——同一 WorkerId 不能被两个活跃实例同时持有。
/// </summary>
public class WorkerIdLeaseGuardTests
{
    private static (ServiceProvider Sp, string DbFile) BuildProvider()
    {
        var id = $"wlg-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new AdminIdOptions { WorkerId = 0 });
        services.AddSingleton(new AdminJobsOptions());
        services.AddTenonAdminSqlSugar(new AdminDatabaseOptions
        {
            DbType = TestDb.DbType,
            ConnectionString = TestDb.ConnectionString(id, dbFile),
        });
        return (services.BuildServiceProvider(), dbFile);
    }

    [Fact]
    public async Task Second_instance_with_same_worker_id_throws()
    {
        var (sp1, dbFile) = BuildProvider();
        await using (sp1)
        {
            var db = sp1.GetRequiredService<ISqlSugarClient>();
            var guard1 = new WorkerIdLeaseGuard(
                db,
                new AdminIdOptions { WorkerId = 0 },
                new AdminJobsOptions(),
                sp1.GetRequiredService<ILoggerFactory>().CreateLogger<WorkerIdLeaseGuard>());

            await guard1.StartAsync(CancellationToken.None);

            // Second guard with same WorkerId should throw
            var guard2 = new WorkerIdLeaseGuard(
                db,
                new AdminIdOptions { WorkerId = 0 },
                new AdminJobsOptions(),
                sp1.GetRequiredService<ILoggerFactory>().CreateLogger<WorkerIdLeaseGuard>());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => guard2.StartAsync(CancellationToken.None));
            Assert.Contains("WorkerId", ex.Message);

            await guard1.StopAsync(CancellationToken.None);
            guard1.Dispose();
            guard2.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public async Task Stop_releases_lease_allowing_new_instance()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var guard1 = new WorkerIdLeaseGuard(
                db,
                new AdminIdOptions { WorkerId = 0 },
                new AdminJobsOptions(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<WorkerIdLeaseGuard>());

            await guard1.StartAsync(CancellationToken.None);
            await guard1.StopAsync(CancellationToken.None);
            guard1.Dispose();

            // After stop, same WorkerId should be claimable
            var guard2 = new WorkerIdLeaseGuard(
                db,
                new AdminIdOptions { WorkerId = 0 },
                new AdminJobsOptions(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<WorkerIdLeaseGuard>());

            await guard2.StartAsync(CancellationToken.None);
            await guard2.StopAsync(CancellationToken.None);
            guard2.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    /// <summary>本机不可能存在的 pid:Windows 的 pid 远小于此,Linux 的 pid_max 上限是 4194304。</summary>
    private const int DeadPid = int.MaxValue;

    private static WorkerIdLeaseGuard NewGuard(ServiceProvider sp) => new(
        sp.GetRequiredService<ISqlSugarClient>(),
        new AdminIdOptions { WorkerId = 0 },
        new AdminJobsOptions(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<WorkerIdLeaseGuard>());

    /// <summary>
    /// 造一条残留租约。到期时刻设在一小时后:走的一定是持有者进程判活,而不是 TTL 自然过期。
    /// <paramref name="machineNameColumn"/> 传空 = 模拟升级前写下的、还没有该列的行。
    /// </summary>
    private static async Task SeedLeaseAsync(ISqlSugarClient db, string nodeHost, string machineNameColumn, int pid)
    {
        db.CodeFirst.InitTables<SysWorkerLease>();
        await db.Insertable(new SysWorkerLease
        {
            WorkerId = 0,
            NodeName = $"{nodeHost}#0@deadbeef",
            MachineName = machineNameColumn,
            Pid = pid,
            LeaseExpiresAt = DateTime.Now.AddHours(1),
        }).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Stale_lease_from_dead_process_on_same_host_is_reclaimed()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            await SeedLeaseAsync(db, Environment.MachineName, Environment.MachineName, DeadPid);

            var guard = NewGuard(sp);
            await guard.StartAsync(CancellationToken.None);  // 不抛 = 已接管

            var row = await db.Queryable<SysWorkerLease>().Where(l => l.WorkerId == 0).FirstAsync();
            Assert.Equal(Environment.ProcessId, row.Pid);

            await guard.StopAsync(CancellationToken.None);
            guard.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public async Task Stale_lease_from_another_host_is_not_reclaimed()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            // 别的主机上的 pid 没有可比性——哪怕本机查无此进程,也不能当成对方已死
            var otherHost = $"other-host-{Guid.NewGuid():N}"[..16];
            await SeedLeaseAsync(db, otherHost, otherHost, DeadPid);

            var guard = NewGuard(sp);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => guard.StartAsync(CancellationToken.None));
            Assert.Contains("WorkerId", ex.Message);
            guard.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public async Task Stale_lease_written_before_upgrade_is_reclaimed_via_node_name()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            // 升级前的行没有 MachineName 列,主机名退回从 NodeName 前缀取,升级后第一次重启不必干等 TTL
            await SeedLeaseAsync(db, Environment.MachineName, "", DeadPid);

            var guard = NewGuard(sp);
            await guard.StartAsync(CancellationToken.None);  // 不抛 = 已接管

            await guard.StopAsync(CancellationToken.None);
            guard.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }
}
