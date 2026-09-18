using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 发号 / 抢槽压测：并发生号不重复，并发行文件锁与库表领槽不出现同一 WorkerId。
/// </summary>
public class WorkerIdStressTests
{
    [Fact]
    public void File_lock_parallel_acquire_yields_distinct_slots()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tenon-wid-stress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var bag = new ConcurrentBag<WorkerIdLease>();
        try
        {
            Parallel.For(0, 32, _ => bag.Add(WorkerIdLease.Acquire(dir)));
            var ids = bag.Select(l => l.WorkerId).ToArray();
            Assert.Equal(32, ids.Length);
            Assert.Equal(32, ids.Distinct().Count());
            Assert.Equal(0, ids.Min());
            Assert.Equal(31, ids.Max());
        }
        finally
        {
            foreach (var lease in bag)
                lease.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* 句柄未放干净 */ }
        }
    }

    [Fact]
    public void Two_claimed_generators_parallel_nextid_do_not_collide()
    {
        var identity = $"wid-stress-gen-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var dirA = Path.Combine(Path.GetTempPath(), $"{identity}-a");
        var dirB = Path.Combine(Path.GetTempPath(), $"{identity}-b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        using var sp1 = BuildClaimHost(identity, dbFile, dirA);
        using var sp2 = BuildClaimHost(identity, dbFile, dirB);
        var genA = sp1.GetRequiredService<IIdGenerator>();
        var genB = sp2.GetRequiredService<IIdGenerator>();
        Assert.NotEqual(
            sp1.GetRequiredService<WorkerIdAssignment>().WorkerId,
            sp2.GetRequiredService<WorkerIdAssignment>().WorkerId);

        const int n = 40_000;
        var a = new long[n];
        var b = new long[n];
        Parallel.Invoke(
            () => Parallel.For(0, n, i => a[i] = genA.NextId()),
            () => Parallel.For(0, n, i => b[i] = genB.NextId()));

        Assert.Equal(n, a.Distinct().Count());
        Assert.Equal(n, b.Distinct().Count());
        Assert.Empty(a.Intersect(b));

        TestDb.Cleanup(identity, dbFile);
        TryDelete(dirA);
        TryDelete(dirB);
    }

    [Fact]
    public void Parallel_hosts_claim_unique_worker_ids()
    {
        var identity = $"wid-stress-claim-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        const int hosts = 8;
        var dirs = Enumerable.Range(0, hosts)
            .Select(i => Path.Combine(Path.GetTempPath(), $"{identity}-h{i}"))
            .ToArray();
        foreach (var d in dirs)
            Directory.CreateDirectory(d);

        var providers = new ServiceProvider[hosts];
        var errors = new ConcurrentBag<Exception>();
        try
        {
            Parallel.For(0, hosts, new ParallelOptions { MaxDegreeOfParallelism = hosts }, i =>
            {
                try
                {
                    providers[i] = BuildClaimHost(identity, dbFile, dirs[i]);
                    _ = providers[i].GetRequiredService<WorkerIdAssignment>();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            Assert.True(errors.IsEmpty, string.Join("; ", errors.Select(e => e.Message)));
            var ids = providers.Select(sp => sp.GetRequiredService<WorkerIdAssignment>().WorkerId).ToArray();
            Assert.Equal(hosts, ids.Distinct().Count());
        }
        finally
        {
            foreach (var sp in providers)
                sp?.Dispose();
            TestDb.Cleanup(identity, dbFile);
            foreach (var d in dirs)
                TryDelete(d);
        }
    }

    private static ServiceProvider BuildClaimHost(string identity, string dbFile, string lockDir)
    {
        var cs = TestDb.ConnectionString(identity, dbFile);
        if (!TestDb.UseMySql && !TestDb.UseSqlServer && !TestDb.UsePostgreSql
            && !cs.Contains("Cache=", StringComparison.OrdinalIgnoreCase))
        {
            cs += ";Cache=Shared;Default Timeout=30";
        }

        var db = new AdminDatabaseOptions
        {
            DbType = TestDb.DbType,
            ConnectionString = cs,
            EnableCodeFirst = false,
            EnableSeed = false,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new AdminIdOptions { WorkerIdLockDir = lockDir });
        services.AddSingleton(new AdminJobsOptions());
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(db);
        services.AddTenonAdminSqlSugar(db, [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        return services.BuildServiceProvider();
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* 锁句柄未放干净 */ }
    }
}
