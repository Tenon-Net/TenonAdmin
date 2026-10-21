using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

public class WorkerIdInitializationFailureTests
{
    [Fact]
    public void Failed_assignment_factory_releases_its_database_lease_and_file_lock()
    {
        var identity = $"wid-factory-failure-{Guid.NewGuid():N}";
        var file = Path.Combine(Path.GetTempPath(), identity + ".db");
        var directory = Path.Combine(Path.GetTempPath(), identity);
        var database = DatabaseOptions(identity, file);
        var claimer = new TrackingClaimer();
        try
        {
            var initializer = new FailingInitializer(new DbWorkerIdSlotClaimer(database));
            var services = new ServiceCollection();
            services.AddSingleton(new AdminIdOptions { WorkerIdLockDir = directory });
            services.AddSingleton<IWorkerIdSlotClaimer>(claimer);
            services.AddSingleton<IWorkerIdLeaseInitializer>(initializer);
            services.AddTenonAdminSqlSugar(database);
            using var provider = services.BuildServiceProvider();

            var error = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<IIdGenerator>());
            Assert.Same(initializer.Error, error);
            Assert.Equal(1, initializer.CallCount);
            using var replacement = new DbWorkerIdSlotClaimer(database)
                .Claim(new AdminIdOptions { WorkerIdLockDir = directory });
            Assert.Equal(0, replacement.WorkerId);
        }
        finally
        {
            // 失败实现也须清理，避免回归测试自身留下续租任务。
            claimer.Last?.Dispose();
            TestDb.Cleanup(identity, file);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Already_leased_assignment_does_not_invoke_another_initializer()
    {
        var identity = $"wid-existing-lifetime-{Guid.NewGuid():N}";
        var file = Path.Combine(Path.GetTempPath(), identity + ".db");
        var database = DatabaseOptions(identity, file);
        var claimer = new TrackingClaimer(new DbWorkerIdSlotClaimer(database));
        var initializer = new FailingInitializer();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new AdminIdOptions { WorkerId = 7 });
            services.AddSingleton<IWorkerIdSlotClaimer>(claimer);
            services.AddSingleton<IWorkerIdLeaseInitializer>(initializer);
            services.AddTenonAdminSqlSugar(database);
            using var provider = services.BuildServiceProvider();

            var generator = provider.GetRequiredService<IIdGenerator>();
            Assert.True(generator.NextId() > 0);
            Assert.Equal(0, initializer.CallCount);
            Assert.NotNull(claimer.Last?.Lifetime);
        }
        finally
        {
            claimer.Last?.Dispose();
            TestDb.Cleanup(identity, file);
        }
    }

    [Fact]
    public void Failed_default_initializer_keeps_callers_assignment_for_retry()
    {
        var identity = $"wid-init-retry-{Guid.NewGuid():N}";
        var file = Path.Combine(Path.GetTempPath(), identity + ".db");
        var directory = Path.Combine(Path.GetTempPath(), identity);
        var database = DatabaseOptions(identity, file);
        var assignment = WorkerIdAssignment.Resolve(new AdminIdOptions { WorkerIdLockDir = directory });
        try
        {
            var clock = new SlowFirstConfirmationClock();
            IWorkerIdLeaseInitializer initializer = new DbWorkerIdSlotClaimer(
                database, new AdminJobsOptions { HeartbeatSeconds = 2 }, clock);
            Assert.Throws<InvalidOperationException>(() => initializer.Initialize(assignment));
            Assert.Null(assignment.Lifetime);
            using (var otherFileLock = WorkerIdLease.Acquire(directory))
                Assert.Equal(1, otherFileLock.WorkerId);

            initializer.Initialize(assignment);
            assignment.EnsureActive();
            Assert.True(SnowflakeIdGenerator.ForAssignment(assignment).NextId() > 0);
        }
        finally
        {
            assignment.Dispose();
            TestDb.Cleanup(identity, file);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 7)]
    public void Failed_claim_releases_its_database_slot_and_file_lock(bool fixedSlot, int expectedWorkerId)
    {
        var identity = $"wid-direct-failure-{Guid.NewGuid():N}";
        var file = Path.Combine(Path.GetTempPath(), identity + ".db");
        var directory = Path.Combine(Path.GetTempPath(), identity);
        try
        {
            var database = DatabaseOptions(identity, file);
            var options = new AdminIdOptions
            {
                WorkerId = fixedSlot ? expectedWorkerId : null,
                WorkerIdLockDir = directory,
            };
            var claimer = new DbWorkerIdSlotClaimer(
                database, new AdminJobsOptions { HeartbeatSeconds = 2 }, new SlowFirstConfirmationClock());
            Assert.Throws<InvalidOperationException>(() => claimer.Claim(options));

            using var replacement = claimer.Claim(options);
            Assert.Equal(expectedWorkerId, replacement.WorkerId);
            replacement.EnsureActive();
        }
        finally
        {
            TestDb.Cleanup(identity, file);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static AdminDatabaseOptions DatabaseOptions(string identity, string file) => new()
    {
        DbType = TestDb.DbType,
        ConnectionString = TestDb.ConnectionString(identity, file),
        EnableCodeFirst = false,
        EnableSeed = false,
    };

    private sealed class TrackingClaimer(IWorkerIdSlotClaimer? inner = null) : IWorkerIdSlotClaimer
    {
        public WorkerIdAssignment? Last { get; private set; }

        public WorkerIdAssignment Claim(AdminIdOptions? options, WorkerIdLockDirFallback? onFallback = null) =>
            Last = inner?.Claim(options, onFallback) ?? WorkerIdAssignment.Resolve(options, onFallback);
    }

    private sealed class FailingInitializer(IWorkerIdLeaseInitializer? inner = null) : IWorkerIdLeaseInitializer
    {
        public InvalidOperationException Error { get; } = new("测试初始化失败。");
        public int CallCount { get; private set; }

        public void Initialize(WorkerIdAssignment assignment)
        {
            CallCount++;
            inner?.Initialize(assignment);
            throw Error;
        }
    }

    private sealed class SlowFirstConfirmationClock : TimeProvider
    {
        private int _reads;

        // 首次领取在数据库请求耗时超出 TTL 后才返回；后续读取固定，允许同一 assignment 重试。
        public override long GetTimestamp() =>
            Interlocked.Increment(ref _reads) == 1 ? 0 : TimestampFrequency * 60;
    }
}
