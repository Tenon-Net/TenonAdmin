using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

public class WorkerIdGenerationFenceTests
{
    [Fact]
    public async Task Assignment_disposed_while_generator_reads_clock_prevents_returning_the_id()
    {
        using var assignment = WorkerIdAssignment.Resolve(new AdminIdOptions { WorkerId = 7 });
        using var clock = new PausingClock();
        var generator = SnowflakeIdGenerator.ForAssignment(assignment, clock);
        var generating = Task.Run(generator.NextId);
        try
        {
            Assert.True(clock.Entered.Wait(TimeSpan.FromSeconds(10)));
            assignment.Dispose();
        }
        finally { clock.Continue.Set(); }
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await generating.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Private_sqlite_memory_claim_keeps_the_same_database_alive_for_renewal()
    {
        var database = new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = "Data Source=:memory:" };
        using var assignment = new DbWorkerIdSlotClaimer(database, new AdminJobsOptions { HeartbeatSeconds = 2 })
            .Claim(new AdminIdOptions { WorkerId = 7 });
        Assert.NotNull(assignment.Lifetime);
        await Task.Delay(TimeSpan.FromSeconds(7));
        assignment.EnsureActive();
        Assert.True(SnowflakeIdGenerator.ForAssignment(assignment).NextId() > 0);
    }

    [Fact]
    public void Preregistered_assignment_is_activated_before_default_generator_can_issue_ids()
    {
        var identity = $"wid-preassigned-{Guid.NewGuid():N}";
        var file = Path.Combine(Path.GetTempPath(), identity + ".db");
        using var assignment = WorkerIdAssignment.Resolve(new AdminIdOptions { WorkerId = 7 });
        try
        {
            var database = new AdminDatabaseOptions
            {
                DbType = TestDb.DbType,
                ConnectionString = TestDb.ConnectionString(identity, file),
                EnableCodeFirst = false,
                EnableSeed = false,
            };
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(assignment);
            services.AddSingleton(new AdminJobsOptions());
            services.AddTenonAdminSqlSugar(database);
            services.AddTenonAdminServices();
            using (var provider = services.BuildServiceProvider())
            {
                var generator = provider.GetRequiredService<IIdGenerator>();
                Assert.Same(assignment, provider.GetRequiredService<WorkerIdAssignment>());
                Assert.Same(assignment, Assert.IsType<SnowflakeIdGenerator>(generator).Assignment);
                Assert.NotNull(assignment.Lifetime);
                Assert.True(generator.NextId() > 0);
                Assert.Throws<InvalidOperationException>(() =>
                    new DbWorkerIdSlotClaimer(database).Claim(new AdminIdOptions { WorkerId = 7 }));
            }
        }
        finally
        {
            // 前置实例不由 DI 自动释放，先释放续租再删测试库。
            assignment.Dispose();
            TestDb.Cleanup(identity, file);
        }
    }

    private sealed class PausingClock : TimeProvider, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Continue { get; } = new();

        public override DateTimeOffset GetUtcNow()
        {
            Entered.Set();
            if (!Continue.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("测试未释放发号时钟。");
            return DateTimeOffset.UtcNow;
        }

        public void Dispose()
        {
            Entered.Dispose();
            Continue.Dispose();
        }
    }
}
