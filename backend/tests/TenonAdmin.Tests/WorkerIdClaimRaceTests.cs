using System.Collections.Concurrent;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 回归：多宿主并发直接调用 <see cref="DbWorkerIdSlotClaimer"/> 抢 sys_worker_lease，
/// 反复多轮，要求不抛异常（含 MySQL 死锁 1213）且 WorkerId 两两不同。
/// </summary>
public class WorkerIdClaimRaceTests
{
    [Fact]
    public void Repeated_parallel_claims_never_throw_and_stay_unique()
    {
        const int hosts = 8;
        var rounds = int.TryParse(Environment.GetEnvironmentVariable("TENON_WID_RACE_ROUNDS"), out var r) ? r : 15;
        for (var round = 0; round < rounds; round++)
        {
            var identity = $"wid-race-{Guid.NewGuid():N}";
            var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
            var cs = TestDb.ConnectionString(identity, dbFile);
            if (!TestDb.UseMySql && !TestDb.UseSqlServer && !TestDb.UsePostgreSql
                && !cs.Contains("Cache=", StringComparison.OrdinalIgnoreCase))
                cs += ";Cache=Shared;Default Timeout=30";
            var db = new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = cs };
            var dirs = Enumerable.Range(0, hosts)
                .Select(i => Path.Combine(Path.GetTempPath(), $"{identity}-h{i}")).ToArray();
            foreach (var d in dirs) Directory.CreateDirectory(d);

            var results = new WorkerIdAssignment?[hosts];
            var errors = new ConcurrentBag<Exception>();
            using var start = new Barrier(hosts);
            try
            {
                Parallel.For(0, hosts, new ParallelOptions { MaxDegreeOfParallelism = hosts }, i =>
                {
                    try
                    {
                        var claimer = new DbWorkerIdSlotClaimer(db, new AdminJobsOptions());
                        start.SignalAndWait();
                        results[i] = claimer.Claim(new AdminIdOptions { WorkerIdLockDir = dirs[i] });
                    }
                    catch (Exception ex) { errors.Add(ex); }
                });

                Assert.True(errors.IsEmpty, $"round {round}: " + string.Join("; ", errors.Select(e => e.Message)));
                var ids = results.Select(a => a!.WorkerId).ToArray();
                Assert.Equal(hosts, ids.Distinct().Count());
            }
            finally
            {
                foreach (var a in results) a?.Dispose();
                TestDb.Cleanup(identity, dbFile);
                if (TestDb.UseMySql) MySqlConnector.MySqlConnection.ClearAllPools();
                foreach (var d in dirs) { try { Directory.Delete(d, true); } catch { } }
            }
        }
    }

    [Fact]
    public void Lock_conflicts_are_recognised_through_wrappers_but_other_errors_are_not()
    {
        var deadlock = new InvalidOperationException("wrap",
            new Exception("Deadlock found when trying to get lock; try restarting transaction"));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(deadlock));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new Exception("Lock wait timeout exceeded; try restarting transaction")));
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(new Exception("Duplicate entry '1' for key 'PRIMARY'")));
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(new Exception("Too many connections")));
    }
}
