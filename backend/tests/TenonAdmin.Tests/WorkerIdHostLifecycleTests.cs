using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 真实 Worker Host 的 WorkerId 租约必须覆盖 Build、Starting 与普通 HostedService 启动阶段。
/// </summary>
public class WorkerIdHostLifecycleTests
{
    private const int HeartbeatSeconds = 2;
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(HeartbeatSeconds * 3);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 7)]
    public async Task Worker_host_keeps_resolved_id_lease_while_start_is_delayed(
        bool explicitWorkerId,
        int expectedWorkerId)
    {
        var identity = $"wid-host-delay-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        using var hostLockDir = new TempLockDir();
        using var rivalLockDir = new TempLockDir();
        IHost? host = null;
        WorkerIdAssignment? rivalAssignment = null;

        try
        {
            host = BuildWorkerHost(
                identity,
                dbFile,
                explicitWorkerId ? expectedWorkerId : null,
                hostLockDir.Path);
            Assert.Single(host.Services.GetServices<IHostedService>().OfType<WorkerIdLeaseGuard>());
            var generator = Assert.IsType<SnowflakeIdGenerator>(host.Services.GetRequiredService<IIdGenerator>());
            var assignment = host.Services.GetRequiredService<WorkerIdAssignment>();
            Assert.Equal(expectedWorkerId, assignment.WorkerId);
            Assert.True(generator.NextId() > 0);

            await Task.Delay(LeaseTtl + TimeSpan.FromSeconds(1));

            var rivalClaimer = new DbWorkerIdSlotClaimer(
                DatabaseOptions(identity, dbFile),
                JobsOptions());
            if (explicitWorkerId)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    rivalClaimer.Claim(new AdminIdOptions { WorkerId = expectedWorkerId }));
            }
            else
            {
                rivalAssignment = rivalClaimer.Claim(new AdminIdOptions
                {
                    WorkerIdLockDir = rivalLockDir.Path,
                });
                Assert.Equal(1, rivalAssignment.WorkerId);
            }

            rivalAssignment?.Dispose();
            rivalAssignment = null;

            await host.StartAsync();
            Assert.True(generator.NextId() > 0);
            await host.StopAsync();
            Assert.Throws<InvalidOperationException>(() => generator.NextId());
        }
        finally
        {
            rivalAssignment?.Dispose();
            await StopAndDisposeAsync(host);
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Guard_claims_custom_assignment_before_earlier_hosted_service_finishes_starting()
    {
        var identity = $"wid-host-custom-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var slowStart = new SlowStartHostedService();
        var customClaimer = new ResolveOnlyClaimer();
        IHost? host = null;
        Task? startTask = null;

        try
        {
            host = BuildWorkerHost(
                identity,
                dbFile,
                workerId: 7,
                lockDir: null,
                beforeWorker: services =>
                {
                    services.AddSingleton<IHostedService>(slowStart);
                    services.AddSingleton<IWorkerIdSlotClaimer>(customClaimer);
                });

            startTask = host.StartAsync();
            await slowStart.StartEntered.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(LeaseTtl + TimeSpan.FromSeconds(1));
            Assert.False(startTask.IsCompleted);

            var rivalClaimer = new DbWorkerIdSlotClaimer(
                DatabaseOptions(identity, dbFile),
                JobsOptions());
            Assert.Throws<InvalidOperationException>(() =>
                rivalClaimer.Claim(new AdminIdOptions { WorkerId = 7 }));

            Assert.False(startTask.IsCompleted);
            slowStart.AllowStart();
            await startTask;
            Assert.Equal(1, customClaimer.CallCount);
            Assert.True(host.Services.GetRequiredService<IIdGenerator>().NextId() > 0);
        }
        finally
        {
            slowStart.AllowStart();
            if (startTask is not null)
            {
                try { await startTask; }
                catch { /* 主断言保留原始失败；Host 仍在下方尽力停止。 */ }
            }
            await StopAndDisposeAsync(host);
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pre_registered_custom_generator_bypasses_worker_id_for_either_layer_order(
        bool servicesFirst)
    {
        var identity = $"wid-host-custom-id-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        using var assignment = WorkerIdAssignment.Resolve(new AdminIdOptions { WorkerId = 7 });
        var generator = new StubIdGenerator();
        var claimer = new ThrowingClaimer();
        IHost? host = null;

        try
        {
            host = BuildWorkerHost(
                identity,
                dbFile,
                workerId: 7,
                lockDir: null,
                beforeWorker: services =>
                {
                    services.AddSingleton(assignment);
                    services.AddSingleton<IIdGenerator>(generator);
                    services.AddSingleton<IWorkerIdSlotClaimer>(claimer);
                    if (servicesFirst)
                        services.AddTenonAdminServices();
                });

            await host.StartAsync();

            Assert.Single(host.Services.GetServices<IHostedService>().OfType<WorkerIdLeaseGuard>());
            Assert.Same(generator, host.Services.GetRequiredService<IIdGenerator>());
            Assert.Same(assignment, host.Services.GetRequiredService<WorkerIdAssignment>());
            Assert.Null(assignment.Lifetime);
            Assert.Equal(0, claimer.CallCount);
            Assert.True(generator.NextId() > 0);
            var db = host.Services.GetRequiredService<ISqlSugarClient>();
            Assert.False(await db.Queryable<SysWorkerLease>()
                .Where(l => l.WorkerId == 7)
                .AnyAsync());
        }
        finally
        {
            await StopAndDisposeAsync(host);
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Default_initializer_is_idempotent_under_concurrent_calls()
    {
        var identity = $"wid-initializer-race-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var database = DatabaseOptions(identity, dbFile);
        var assignment = WorkerIdAssignment.Resolve(new AdminIdOptions { WorkerId = 7 });
        WorkerIdAssignment? reclaimed = null;
        ServiceProvider? provider = null;

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new AdminIdOptions { WorkerId = 7 });
            services.AddSingleton(JobsOptions());
            services.AddSingleton(new AdminCacheOptions());
            services.AddSingleton(database);
            services.AddSingleton(assignment);
            services.AddTenonAdminServices();
            provider = services.BuildServiceProvider();

            var initializer = provider.GetRequiredService<IWorkerIdLeaseInitializer>();
            var observed = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                initializer.Initialize(assignment);
                return assignment.Lifetime;
            })));

            var lifetime = Assert.IsAssignableFrom<IWorkerIdLeaseLifetime>(assignment.Lifetime);
            Assert.All(observed, candidate => Assert.Same(lifetime, candidate));

            await Task.Delay(LeaseTtl + TimeSpan.FromSeconds(1));
            assignment.EnsureActive();
            var rivalClaimer = new DbWorkerIdSlotClaimer(database, JobsOptions());
            Assert.Throws<InvalidOperationException>(() =>
                rivalClaimer.Claim(new AdminIdOptions { WorkerId = 7 }));

            assignment.Dispose();
            reclaimed = rivalClaimer.Claim(new AdminIdOptions { WorkerId = 7 });
            Assert.Equal(7, reclaimed.WorkerId);
            reclaimed.EnsureActive();
        }
        finally
        {
            reclaimed?.Dispose();
            assignment.Dispose();
            provider?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    private static IHost BuildWorkerHost(
        string identity,
        string dbFile,
        int? workerId,
        string? lockDir,
        Action<IServiceCollection>? beforeWorker = null)
    {
        var database = DatabaseOptions(identity, dbFile);
        var settings = new Dictionary<string, string?>
        {
            ["TenonAdmin:Database:DbType"] = database.DbType,
            ["TenonAdmin:Database:ConnectionString"] = database.ConnectionString,
            ["TenonAdmin:Database:EnableCodeFirst"] = "true",
            ["TenonAdmin:Database:EnableSeed"] = "false",
            ["TenonAdmin:Jobs:SchedulerEnabled"] = "false",
            ["TenonAdmin:Jobs:HeartbeatSeconds"] = HeartbeatSeconds.ToString(),
            ["TenonAdmin:Jobs:LeaseSeconds"] = (HeartbeatSeconds * 3).ToString(),
        };
        if (workerId is not null)
            settings["TenonAdmin:Id:WorkerId"] = workerId.Value.ToString();
        if (lockDir is not null)
            settings["TenonAdmin:Id:WorkerIdLockDir"] = lockDir;

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(settings);
        // Worker 组合根只运行调度器，但 Services 层仍注册了认证服务；Development 的
        // ValidateOnBuild 会验证完整依赖图，因此为这些不会在本测试执行的服务补齐合法扩展点。
        builder.Services.AddSingleton(new AdminJwtOptions());
        builder.Services.AddSingleton<ITokenProvider, UnusedTokenProvider>();
        beforeWorker?.Invoke(builder.Services);
        builder.Services.AddTenonAdminWorker(builder.Configuration);
        return builder.Build();
    }

    private static AdminDatabaseOptions DatabaseOptions(string identity, string dbFile) => new()
    {
        DbType = TestDb.DbType,
        ConnectionString = TestDb.ConnectionString(identity, dbFile),
        EnableCodeFirst = true,
        EnableSeed = false,
    };

    private static AdminJobsOptions JobsOptions() => new()
    {
        HeartbeatSeconds = HeartbeatSeconds,
        LeaseSeconds = HeartbeatSeconds * 3,
        SchedulerEnabled = false,
    };

    private static async Task StopAndDisposeAsync(IHost? host)
    {
        if (host is null) return;
        try { await host.StopAsync(); }
        catch { /* 清理不掩盖主断言。 */ }
        host.Dispose();
    }

    private sealed class SlowStartHostedService : IHostedService
    {
        private readonly TaskCompletionSource _startEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _continueStart = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartEntered => _startEntered.Task;
        public void AllowStart() => _continueStart.TrySetResult();

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _startEntered.TrySetResult();
            // 主断言完成后才放行，确保高负载时也不会与后续 CodeFirst 事务交叠。
            await _continueStart.Task.WaitAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ResolveOnlyClaimer : IWorkerIdSlotClaimer
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public WorkerIdAssignment Claim(
            AdminIdOptions? options,
            WorkerIdLockDirFallback? onFallback = null)
        {
            Interlocked.Increment(ref _callCount);
            return WorkerIdAssignment.Resolve(options, onFallback);
        }
    }

    private sealed class ThrowingClaimer : IWorkerIdSlotClaimer
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public WorkerIdAssignment Claim(
            AdminIdOptions? options,
            WorkerIdLockDirFallback? onFallback = null)
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("自定义发号器路径不应调用 WorkerId claimer。");
        }
    }

    private sealed class StubIdGenerator : IIdGenerator
    {
        private long _last = SnowflakeIdGenerator.CurrentFloor();

        public long NextId() => Interlocked.Increment(ref _last);
    }

    private sealed class UnusedTokenProvider : ITokenProvider
    {
        public TokenPair Create(TokenSubject subject) =>
            throw new NotSupportedException("WorkerId 生命周期测试不会签发令牌。");
    }

    private sealed class TempLockDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"tenon-wid-host-{Guid.NewGuid():N}");

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
