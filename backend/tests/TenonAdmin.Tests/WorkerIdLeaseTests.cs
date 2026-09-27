using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 同机雪花机器号文件锁:未显式 WorkerId 时自动错开 0–63,释放只关句柄不删文件。
/// </summary>
public class WorkerIdLeaseTests
{
    [Fact]
    public void Acquire_second_process_gets_next_slot()
    {
        using var dir = new TempLockDir();
        using var first = WorkerIdLease.Acquire(dir.Path);
        using var second = WorkerIdLease.Acquire(dir.Path);

        Assert.Equal(0, first.WorkerId);
        Assert.Equal(1, second.WorkerId);
        Assert.True(File.Exists(first.LockFile));
        Assert.True(File.Exists(second.LockFile));
    }

    [Fact]
    public void Dispose_releases_slot_without_deleting_lock_file()
    {
        using var dir = new TempLockDir();
        string lockFile;
        using (var first = WorkerIdLease.Acquire(dir.Path))
        {
            Assert.Equal(0, first.WorkerId);
            lockFile = first.LockFile;
        }

        Assert.True(File.Exists(lockFile), "释放不得删除锁文件,否则 Linux 上同路径会新建 inode");

        using var again = WorkerIdLease.Acquire(dir.Path);
        Assert.Equal(0, again.WorkerId);
        Assert.Equal(lockFile, again.LockFile);
    }

    [Fact]
    public void Acquire_throws_when_all_slots_are_taken()
    {
        using var dir = new TempLockDir();
        var held = new List<WorkerIdLease>();
        try
        {
            for (var i = 0; i <= SnowflakeIdGenerator.MaxWorkerId; i++)
                held.Add(WorkerIdLease.Acquire(dir.Path));

            var ex = Assert.Throws<InvalidOperationException>(() => WorkerIdLease.Acquire(dir.Path));
            Assert.Contains("拒绝发号", ex.Message);
            Assert.Contains(dir.Path, ex.Message);
        }
        finally
        {
            foreach (var lease in held)
                lease.Dispose();
        }
    }

    [Fact]
    public void Acquire_throws_when_specified_directory_is_not_writable()
    {
        var blocker = Path.Combine(Path.GetTempPath(), $"tenon-wid-block-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "not-a-directory");
        var lockDir = Path.Combine(blocker, "workerid");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => WorkerIdLease.Acquire(lockDir));
            Assert.Contains("不可用", ex.Message);
            Assert.Contains("WorkerIdLockDir", ex.Message);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void Resolve_skips_file_lock_when_worker_id_is_explicit()
    {
        using var dir = new TempLockDir();
        var assignment = WorkerIdAssignment.Resolve(new AdminIdOptions
        {
            WorkerId = 3,
            WorkerIdLockDir = dir.Path,
        });

        using (assignment)
        {
            Assert.Equal(3, assignment.WorkerId);
            Assert.Null(assignment.Lease);
            Assert.Empty(Directory.GetFiles(dir.Path));
        }
    }

    [Fact]
    public void Resolve_without_options_falls_back_to_zero_without_lock()
    {
        using var assignment = WorkerIdAssignment.Resolve(null);
        Assert.Equal(0, assignment.WorkerId);
        Assert.Null(assignment.Lease);
    }

    [Fact]
    public void Two_hosts_same_database_unset_worker_id_claim_distinct_slots()
    {
        var identity = $"wid-two-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        using var dirA = new TempLockDir();
        using var dirB = new TempLockDir();

        using var sp1 = BuildClaimHost(identity, dbFile, dirA.Path);
        using var sp2 = BuildClaimHost(identity, dbFile, dirB.Path);

        var a = sp1.GetRequiredService<WorkerIdAssignment>();
        var b = sp2.GetRequiredService<WorkerIdAssignment>();
        Assert.Equal(0, a.WorkerId);
        Assert.Equal(1, b.WorkerId);
        Assert.NotEqual(a.WorkerId, b.WorkerId);

        var idA = sp1.GetRequiredService<IIdGenerator>().NextId();
        var idB = sp2.GetRequiredService<IIdGenerator>().NextId();
        Assert.NotEqual(idA, idB);

        TestDb.Cleanup(identity, dbFile);
    }

    private static ServiceProvider BuildClaimHost(string identity, string dbFile, string lockDir)
    {
        var db = new AdminDatabaseOptions
        {
            DbType = TestDb.DbType,
            ConnectionString = TestDb.ConnectionString(identity, dbFile),
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

    [Fact]
    public void SqlSugar_composition_does_not_acquire_when_worker_id_is_explicit()
    {
        using var dir = new TempLockDir();
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-wid-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AdminIdOptions { WorkerId = 5, WorkerIdLockDir = dir.Path });
        var db = new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" };
        services.AddSingleton(db);
        services.AddTenonAdminSqlSugar(db);

        using var sp = services.BuildServiceProvider();
        var gen = sp.GetRequiredService<IIdGenerator>();
        var assignment = sp.GetRequiredService<WorkerIdAssignment>();

        Assert.Equal(5, assignment.WorkerId);
        Assert.Null(assignment.Lease);
        Assert.True(gen.NextId() > 0);
        Assert.Empty(Directory.Exists(dir.Path) ? Directory.GetFiles(dir.Path) : []);

        TestDb.Cleanup(dbFile, dbFile);
    }

    private sealed class TempLockDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"tenon-wid-{Guid.NewGuid():N}");

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
                /* 仍持有句柄时留给临时目录 */
            }
        }
    }
}
