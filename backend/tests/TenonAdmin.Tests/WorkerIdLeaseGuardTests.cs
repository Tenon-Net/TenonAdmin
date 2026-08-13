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
}
