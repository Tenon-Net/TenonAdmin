using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// WorkerId 数据库租约与默认发号器必须共享同一段有效生命周期。
/// </summary>
public class WorkerIdLifetimeTests
{
    private const int HeartbeatSeconds = 2;
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(HeartbeatSeconds * 3);

    [Fact]
    public async Task Bare_claim_keeps_database_slot_until_assignment_is_disposed()
    {
        var identity = $"wid-life-claim-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        using var dirA = new TempLockDir();
        using var dirB = new TempLockDir();
        using var dirC = new TempLockDir();
        WorkerIdAssignment? first = null;
        WorkerIdAssignment? second = null;
        WorkerIdAssignment? afterDispose = null;

        try
        {
            var database = DatabaseOptions(identity, dbFile);
            var jobs = JobsOptions();
            var firstClaimer = new DbWorkerIdSlotClaimer(database, jobs);
            var secondClaimer = new DbWorkerIdSlotClaimer(database, jobs);
            var thirdClaimer = new DbWorkerIdSlotClaimer(database, jobs);

            first = firstClaimer.Claim(new AdminIdOptions { WorkerIdLockDir = dirA.Path });
            Assert.Equal(0, first.WorkerId);

            await Task.Delay(LeaseTtl + TimeSpan.FromSeconds(1));

            second = secondClaimer.Claim(new AdminIdOptions { WorkerIdLockDir = dirB.Path });
            Assert.Equal(1, second.WorkerId);

            first.Dispose();
            afterDispose = thirdClaimer.Claim(new AdminIdOptions { WorkerIdLockDir = dirC.Path });
            Assert.Equal(0, afterDispose.WorkerId);
        }
        finally
        {
            afterDispose?.Dispose();
            second?.Dispose();
            first?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Default_generator_rejects_ids_after_lease_owner_is_replaced()
    {
        var identity = $"wid-life-takeover-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        using var lockDir = new TempLockDir();
        ServiceProvider? provider = null;

        try
        {
            provider = BuildDefaultProvider(identity, dbFile, lockDir.Path);
            var generator = provider.GetRequiredService<IIdGenerator>();
            var assignment = provider.GetRequiredService<WorkerIdAssignment>();
            var db = provider.GetRequiredService<ISqlSugarClient>();
            var workerId = (int)assignment.WorkerId;

            var rows = await db.Updateable<SysWorkerLease>()
                .SetColumns(l => new SysWorkerLease
                {
                    NodeName = $"{Environment.MachineName}#{workerId}@takeover",
                    MachineName = Environment.MachineName,
                    Pid = Environment.ProcessId,
                    LeaseExpiresAt = DateTime.Now.Add(LeaseTtl),
                })
                .Where(l => l.WorkerId == workerId)
                .ExecuteCommandAsync();
            Assert.Equal(1, rows);

            await Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds + 1));

            Assert.Throws<InvalidOperationException>(() => generator.NextId());
        }
        finally
        {
            provider?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Default_generator_rejects_ids_after_renewal_fails_past_confirmed_expiry()
    {
        var identity = $"wid-life-renew-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        using var lockDir = new TempLockDir();
        ServiceProvider? provider = null;

        try
        {
            provider = BuildDefaultProvider(identity, dbFile, lockDir.Path);
            var generator = provider.GetRequiredService<IIdGenerator>();
            var db = provider.GetRequiredService<ISqlSugarClient>();
            _ = provider.GetRequiredService<WorkerIdAssignment>();

            db.DbMaintenance.DropTable<SysWorkerLease>();
            await Task.Delay(LeaseTtl + TimeSpan.FromSeconds(1));

            Assert.Throws<InvalidOperationException>(() => generator.NextId());
        }
        finally
        {
            provider?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public void Default_generator_rejects_ids_after_assignment_is_disposed()
    {
        var identity = $"wid-life-dispose-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        using var lockDir = new TempLockDir();
        ServiceProvider? provider = null;

        try
        {
            provider = BuildDefaultProvider(identity, dbFile, lockDir.Path);
            var generator = provider.GetRequiredService<IIdGenerator>();
            var assignment = provider.GetRequiredService<WorkerIdAssignment>();

            assignment.Dispose();

            Assert.Throws<InvalidOperationException>(() => generator.NextId());
        }
        finally
        {
            provider?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Theory]
    [InlineData(true, 7)]
    [InlineData(false, 0)]
    public async Task Fixed_worker_id_is_leased_without_starting_a_host(
        bool registerIdOptions,
        int expectedWorkerId)
    {
        var identity = $"wid-life-fixed-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        ServiceProvider? first = null;
        ServiceProvider? second = null;
        ServiceProvider? third = null;

        try
        {
            first = BuildFixedProvider(identity, dbFile, registerIdOptions, expectedWorkerId);
            var firstGenerator = first.GetRequiredService<IIdGenerator>();
            Assert.Equal(expectedWorkerId, first.GetRequiredService<WorkerIdAssignment>().WorkerId);
            Assert.True(firstGenerator.NextId() > 0);

            await Task.Delay(LeaseTtl + TimeSpan.FromSeconds(1));

            second = BuildFixedProvider(identity, dbFile, registerIdOptions, expectedWorkerId);
            Assert.Throws<InvalidOperationException>(() =>
                second.GetRequiredService<IIdGenerator>().NextId());

            first.Dispose();
            first = null;

            third = BuildFixedProvider(identity, dbFile, registerIdOptions, expectedWorkerId);
            var thirdGenerator = third.GetRequiredService<IIdGenerator>();
            Assert.Equal(expectedWorkerId, third.GetRequiredService<WorkerIdAssignment>().WorkerId);
            Assert.True(thirdGenerator.NextId() > 0);
        }
        finally
        {
            third?.Dispose();
            second?.Dispose();
            first?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    private static ServiceProvider BuildDefaultProvider(string identity, string dbFile, string lockDir)
    {
        var database = DatabaseOptions(identity, dbFile);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new AdminIdOptions { WorkerIdLockDir = lockDir });
        services.AddSingleton(JobsOptions());
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(database);
        services.AddTenonAdminSqlSugar(database, [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildFixedProvider(
        string identity,
        string dbFile,
        bool registerIdOptions,
        int workerId)
    {
        var database = DatabaseOptions(identity, dbFile);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        if (registerIdOptions)
            services.AddSingleton(new AdminIdOptions { WorkerId = workerId });
        services.AddSingleton(JobsOptions());
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(database);
        services.AddTenonAdminSqlSugar(database, [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        return services.BuildServiceProvider();
    }

    private static AdminDatabaseOptions DatabaseOptions(string identity, string dbFile) => new()
    {
        DbType = TestDb.DbType,
        ConnectionString = TestDb.ConnectionString(identity, dbFile),
        EnableCodeFirst = false,
        EnableSeed = false,
    };

    private static AdminJobsOptions JobsOptions() => new()
    {
        HeartbeatSeconds = HeartbeatSeconds,
        SchedulerEnabled = false,
    };

    private sealed class TempLockDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"tenon-wid-life-{Guid.NewGuid():N}");

        public TempLockDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                /* 仍持有句柄时留给临时目录。 */
            }
        }
    }
}
