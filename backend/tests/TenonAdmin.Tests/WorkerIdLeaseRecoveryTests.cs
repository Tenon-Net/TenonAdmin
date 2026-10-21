using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Npgsql;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// WorkerId 续租被真实数据库锁阻塞时的恢复边界。四种数据库都用独立事务更新租约行持锁，
/// 不替换续租实现、不缩短既有 TTL，也不靠 mock 吞掉真实驱动等待。
/// </summary>
public class WorkerIdLeaseRecoveryTests
{
    private const int WorkerId = 7;
    private const int HeartbeatSeconds = 2;
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(HeartbeatSeconds * 3);

    [Fact]
    public async Task Same_owner_recovers_only_after_a_fresh_confirmation_following_lock_timeout()
    {
        var identity = $"wid-recover-owner-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var database = DatabaseOptions(identity, dbFile);
        var clock = new ControlledTimeProvider();
        LeaseFixture? fixture = null;

        try
        {
            fixture = await LeaseFixture.CreateAsync(database, clock);
            Assert.Equal(WorkerId, fixture.Assignment.WorkerId);
            Assert.True(fixture.Generator.NextId() > 0);

            var before = await ReadLeaseAsync(fixture.ObserverDb);
            await using (var blocker = await LeaseRowBlocker.AcquireAsync(database.ConnectionString))
            {
                // 跨过数据库 CURRENT_TIMESTAMP 的秒精度边界，使迟到 UPDATE 的新到期值可观测。
                await Task.Delay(TimeSpan.FromMilliseconds(1100));
                await clock.ReleaseNextTimerAsync();
                await fixture.SqlProbe.WaitForNextAsync();
                clock.Advance(LeaseTtl + TimeSpan.FromSeconds(1));

                // 续租 UPDATE 仍被真实行锁阻塞；最后确认期限一过，发号器必须立即拒绝。
                Assert.Throws<InvalidOperationException>(() => fixture.Generator.NextId());
                await blocker.CommitAsync();
            }

            // 先等被阻塞的那条 UPDATE 真正返回并落库。它的确认起点早于超期点，属于迟到响应，
            // 即使 rows=1 也不能直接把本地状态恢复成 active。
            await fixture.SqlProbe.WaitForCompletionAsync();
            await WaitUntilAsync(async () =>
                (await ReadLeaseAsync(fixture.ObserverDb)).LeaseExpiresAt > before.LeaseExpiresAt);
            Assert.Throws<InvalidOperationException>(() => fixture.Generator.NextId());

            // 同一 owner 仍持有行；后续一次在期限内完成的新鲜确认才允许恢复发号。
            await clock.ReleaseNextTimerAsync();
            await fixture.SqlProbe.WaitForNextAsync();
            await WaitUntilAsync(() => Task.FromResult(TryNextId(fixture.Generator)));
            Assert.True(fixture.Generator.NextId() > 0);
        }
        finally
        {
            if (fixture is not null) await fixture.DisposeAsync();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Rival_takeover_while_renewal_is_blocked_is_permanently_rejected()
    {
        var identity = $"wid-recover-rival-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var database = DatabaseOptions(identity, dbFile);
        var clock = new ControlledTimeProvider();
        LeaseFixture? fixture = null;

        try
        {
            fixture = await LeaseFixture.CreateAsync(database, clock);
            Assert.True(fixture.Generator.NextId() > 0);

            var originalOwner = await ReadLeaseAsync(fixture.ObserverDb);
            var rivalNode = $"rival#{WorkerId}@{Guid.NewGuid():N}";
            await using (var blocker = await LeaseRowBlocker.AcquireAsync(database.ConnectionString))
            {
                // 变更在事务提交前不可见，同时仍持有同一行的写锁；旧 owner 的续租会排在后面等待。
                await blocker.ReplaceOwnerAsync(rivalNode, DateTime.Now.AddMinutes(1));
                await clock.ReleaseNextTimerAsync();
                await fixture.SqlProbe.WaitForNextAsync();
                clock.Advance(LeaseTtl + TimeSpan.FromSeconds(1));
                Assert.Throws<InvalidOperationException>(() => fixture.Generator.NextId());
                await blocker.CommitAsync();
            }

            await fixture.SqlProbe.WaitForCompletionAsync();
            await fixture.SqlProbe.WaitForOwnershipReadCompletionAsync();
            await WaitUntilAsync(async () =>
                string.Equals((await ReadLeaseAsync(fixture.ObserverDb)).NodeName, rivalNode, StringComparison.Ordinal));

            // 锁释放后旧 UPDATE 因 owner token 不匹配返回 rows=0；后续任何节拍都不得恢复。
            clock.Advance(LeaseTtl * 2);
            Assert.Throws<InvalidOperationException>(() => fixture.Generator.NextId());
            Assert.Equal(rivalNode, (await ReadLeaseAsync(fixture.ObserverDb)).NodeName);
            Assert.NotNull(fixture.Assignment.Lifetime);

            // 故障注入：等 rows=0 后的 owner 回读完成，再把数据库行恢复成原 owner 且给足有效期。
            // 本地 lifetime 已进入永久 Lost；即使库中所有权后来“恢复”，公开 Guard 也不能把它复活。
            var restored = await fixture.ObserverDb.Updateable<SysWorkerLease>()
                .SetColumns(l => new SysWorkerLease
                {
                    NodeName = originalOwner.NodeName,
                    MachineName = originalOwner.MachineName,
                    Pid = originalOwner.Pid,
                    LeaseExpiresAt = DateTime.Now.AddMinutes(1),
                })
                .Where(l => l.WorkerId == WorkerId)
                .ExecuteCommandAsync();
            Assert.Equal(1, restored);
            Assert.Equal(originalOwner.NodeName, (await ReadLeaseAsync(fixture.ObserverDb)).NodeName);

            using var freshGuard = new WorkerIdLeaseGuard(
                fixture.ObserverDb,
                new AdminIdOptions { WorkerId = WorkerId },
                new AdminJobsOptions
                {
                    HeartbeatSeconds = HeartbeatSeconds,
                    LeaseSeconds = HeartbeatSeconds * 3,
                    SchedulerEnabled = false,
                },
                NullLogger<WorkerIdLeaseGuard>.Instance,
                clock,
                fixture.Assignment);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => freshGuard.StartAsync(CancellationToken.None));
        }
        finally
        {
            if (fixture is not null) await fixture.DisposeAsync();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Pre_resolved_generator_is_freshly_confirmed_when_host_starts_after_lock_timeout()
    {
        var identity = $"wid-host-pre-resolved-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var database = DatabaseOptions(identity, dbFile);
        var clock = new ControlledTimeProvider();
        LeaseFixture? fixture = null;
        IHost? host = null;

        try
        {
            fixture = await LeaseFixture.CreateAsync(database, clock);
            host = BuildHost(database, fixture.Assignment, clock);

            // Build 后、Start 前解析真实发号器，复现消费者提前解析单例的合法用法。
            var generator = host.Services.GetRequiredService<IIdGenerator>();
            Assert.True(generator.NextId() > 0);

            Task startTask;
            await using (var blocker = await LeaseRowBlocker.AcquireAsync(database.ConnectionString))
            {
                await clock.ReleaseNextTimerAsync();
                await fixture.SqlProbe.WaitForNextAsync();
                clock.Advance(LeaseTtl + TimeSpan.FromSeconds(1));
                Assert.Throws<InvalidOperationException>(() => generator.NextId());

                await blocker.CommitAsync();
                // 不释放下一轮受控 timer；只能由 Host Guard 的启动确认恢复，后台不能抢先绿。
                startTask = host.StartAsync();
            }

            await startTask;
            await fixture.SqlProbe.WaitForCompletionAsync();
            Assert.True(generator.NextId() > 0);
        }
        finally
        {
            if (host is not null)
            {
                try { await host.StopAsync(); }
                catch { /* 清理不能掩盖主断言。 */ }
                host.Dispose();
            }
            if (fixture is not null) await fixture.DisposeAsync();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Direct_guard_freshly_confirms_initial_claim_delayed_past_local_ttl()
    {
        var identity = $"wid-direct-initial-claim-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var database = DatabaseOptions(identity, dbFile);
        var clock = new ControlledTimeProvider();
        ISqlSugarClient? leaseDb = null;
        ISqlSugarClient? observerDb = null;
        WorkerIdAssignment? assignment = null;
        WorkerIdLeaseGuard? guard = null;
        InitialLeaseUpdateGate? updateGate = null;
        Task? startTask = null;

        try
        {
            leaseDb = OpenClient(database);
            observerDb = OpenClient(database);
            observerDb.CodeFirst.InitTables<SysWorkerLease>();
            await observerDb.Insertable(new SysWorkerLease
            {
                Id = WorkerId + 1,
                WorkerId = WorkerId,
                NodeName = $"expired#{WorkerId}@seed",
                MachineName = "expired-host",
                Pid = -1,
                LeaseExpiresAt = DateTime.UnixEpoch,
            }).ExecuteCommandAsync();

            // 公开构造函数的 assignment 参数虽可省略；此处显式传入，才能从公开发号器观察
            // StartAsync 成功返回时 lifetime 是否已经真正可用。
            assignment = WorkerIdAssignment.Resolve(
                new AdminIdOptions { WorkerId = WorkerId });
            var generator = SnowflakeIdGenerator.ForAssignment(assignment, clock);
            updateGate = new InitialLeaseUpdateGate();
            leaseDb.Aop.OnLogExecuting = updateGate.OnExecuting;
            guard = new WorkerIdLeaseGuard(
                leaseDb,
                new AdminIdOptions { WorkerId = WorkerId },
                new AdminJobsOptions
                {
                    HeartbeatSeconds = HeartbeatSeconds,
                    LeaseSeconds = HeartbeatSeconds * 3,
                    SchedulerEnabled = false,
                },
                NullLogger<WorkerIdLeaseGuard>.Instance,
                clock,
                assignment);

            // SQLite 的 SqlSugar async 路径可能同步阻塞；放到独立线程，确保测试能控制 AOP 闸门。
            startTask = Task.Run(() => guard.StartAsync(CancellationToken.None));
            var updateReached = updateGate.WaitUntilReachedAsync();
            if (await Task.WhenAny(startTask, updateReached) == startTask)
                await startTask;
            await updateReached;

            // 初始 SELECT 已完成、UPDATE 尚未进入驱动，此时再锁行，避免 SQL Server 的 SELECT 先被挡住。
            await using (var blocker = await LeaseRowBlocker.AcquireAsync(database.ConnectionString))
            {
                updateGate.Release();
                await Task.Delay(100);
                clock.Advance(LeaseTtl + TimeSpan.FromSeconds(1));
                await blocker.CommitAsync();
            }

            // StartAsync 返回成功时必须已完成新鲜确认，不能交付一个马上拒绝发号的 lifetime。
            await startTask;
            Assert.True(generator.NextId() > 0);
        }
        finally
        {
            updateGate?.Release();
            if (startTask is not null)
            {
                try { await startTask; }
                catch { /* 主测试中的首次异常仍会向外传播；这里只确保后台不遗留。 */ }
            }
            if (guard is not null)
            {
                try { await guard.StopAsync(CancellationToken.None); }
                catch { /* 清理不能掩盖主断言。 */ }
                guard.Dispose();
            }
            assignment?.Dispose();
            updateGate?.Dispose();
            leaseDb?.Dispose();
            observerDb?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Fresh_confirmation_accepts_owned_row_when_provider_reports_zero_changed_rows()
    {
        var identity = $"wid-fresh-zero-changed-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var database = DatabaseOptions(identity, dbFile);
        var clock = new ControlledTimeProvider();
        ISqlSugarClient? leaseDb = null;
        ISqlSugarClient? observerDb = null;
        WorkerIdAssignment? assignment = null;
        WorkerIdLeaseGuard? guard = null;

        try
        {
            observerDb = OpenClient(database);
            observerDb.CodeFirst.InitTables<SysWorkerLease>();
            leaseDb = OpenPinnedLeaseClient(database);
            if (TestDb.UseMySql)
            {
                // 仅固定本测试连接的服务器时间，确保两次确认写入完全相同的到期值。
                leaseDb.Ado.ExecuteCommand("SET timestamp = UNIX_TIMESTAMP()");
            }

            assignment = WorkerIdAssignment.Resolve(
                new AdminIdOptions { WorkerId = WorkerId });
            var generator = SnowflakeIdGenerator.ForAssignment(assignment, clock);
            guard = new WorkerIdLeaseGuard(
                leaseDb,
                new AdminIdOptions { WorkerId = WorkerId },
                new AdminJobsOptions
                {
                    HeartbeatSeconds = HeartbeatSeconds,
                    LeaseSeconds = HeartbeatSeconds * 3,
                    SchedulerEnabled = false,
                },
                NullLogger<WorkerIdLeaseGuard>.Instance,
                clock,
                assignment);

            await guard.StartingAsync(CancellationToken.None);
            var before = await ReadLeaseAsync(observerDb);

            // Host 普通 Start 强制 fresh owner CAS；MySQL changed-rows 语义会为同值 UPDATE 返回 0。
            await guard.StartAsync(CancellationToken.None);

            var after = await ReadLeaseAsync(observerDb);
            if (TestDb.UseMySql)
                Assert.Equal(before.LeaseExpiresAt, after.LeaseExpiresAt);
            else
                Assert.True(after.LeaseExpiresAt >= before.LeaseExpiresAt);
            Assert.True(generator.NextId() > 0);
        }
        finally
        {
            if (guard is not null)
            {
                try { await guard.StopAsync(CancellationToken.None); }
                catch { /* 清理不能掩盖主断言。 */ }
                guard.Dispose();
            }
            assignment?.Dispose();
            leaseDb?.Dispose();
            observerDb?.Dispose();
            TestDb.Cleanup(identity, dbFile);
        }
    }

    [Fact]
    public async Task Custom_lifetime_uses_its_public_contract_through_both_host_stages()
    {
        using var assignment = WorkerIdAssignment.Resolve(new AdminIdOptions { WorkerId = WorkerId });
        var lifetime = new ConsumerLifetime();
        assignment.AttachLifetime(lifetime);
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = "Data Source=:memory:",
            IsAutoCloseConnection = false,
        });
        db.Aop.OnLogExecuting = (_, _) =>
            throw new InvalidOperationException("自定义 lifetime 不应触发默认数据库领取或确认。");
        using var guard = new WorkerIdLeaseGuard(
            db, new AdminIdOptions { WorkerId = WorkerId }, new AdminJobsOptions(),
            NullLogger<WorkerIdLeaseGuard>.Instance, assignment: assignment);
        var generator = SnowflakeIdGenerator.ForAssignment(assignment);

        var checks = lifetime.CheckCount;
        await guard.StartingAsync(CancellationToken.None);
        Assert.True(lifetime.CheckCount > checks);
        checks = lifetime.CheckCount;
        await guard.StartAsync(CancellationToken.None);
        Assert.True(lifetime.CheckCount > checks);
        Assert.True(generator.NextId() > 0);

        await guard.StopAsync(CancellationToken.None);
        Assert.True(lifetime.Disposed);
        Assert.Throws<InvalidOperationException>(() => generator.NextId());
    }

    private static bool TryNextId(IIdGenerator generator)
    {
        try
        {
            return generator.NextId() > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<SysWorkerLease> ReadLeaseAsync(ISqlSugarClient db) =>
        await db.Queryable<SysWorkerLease>().Where(l => l.WorkerId == WorkerId).FirstAsync()
        ?? throw new InvalidOperationException("WorkerId 租约行不存在。");

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(25);
        }

        Assert.True(await predicate(), "等待 WorkerId 租约状态收敛超时。");
    }

    private static AdminDatabaseOptions DatabaseOptions(string identity, string dbFile) => new()
    {
        DbType = TestDb.DbType,
        ConnectionString = TestDb.ConnectionString(identity, dbFile),
        EnableCodeFirst = false,
        EnableSeed = false,
    };

    private static IHost BuildHost(
        AdminDatabaseOptions database,
        WorkerIdAssignment assignment,
        ControlledTimeProvider clock)
    {
        var settings = new Dictionary<string, string?>
        {
            ["TenonAdmin:Database:DbType"] = database.DbType,
            ["TenonAdmin:Database:ConnectionString"] = database.ConnectionString,
            ["TenonAdmin:Database:EnableCodeFirst"] = "true",
            ["TenonAdmin:Database:EnableSeed"] = "false",
            ["TenonAdmin:Jobs:SchedulerEnabled"] = "false",
            ["TenonAdmin:Jobs:HeartbeatSeconds"] = HeartbeatSeconds.ToString(),
            ["TenonAdmin:Jobs:LeaseSeconds"] = (HeartbeatSeconds * 3).ToString(),
            ["TenonAdmin:Id:WorkerId"] = WorkerId.ToString(),
        };

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(new AdminJwtOptions());
        builder.Services.AddSingleton<ITokenProvider, UnusedTokenProvider>();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(assignment);
        builder.Services.AddTenonAdminWorker(builder.Configuration);
        return builder.Build();
    }

    private sealed class LeaseFixture : IAsyncDisposable
    {
        private readonly WorkerIdLeaseGuard _guard;
        private readonly ISqlSugarClient _leaseDb;

        public WorkerIdAssignment Assignment { get; }
        public SnowflakeIdGenerator Generator { get; }
        public ISqlSugarClient ObserverDb { get; }
        public RenewalSqlProbe SqlProbe { get; }

        private LeaseFixture(
            WorkerIdAssignment assignment,
            SnowflakeIdGenerator generator,
            WorkerIdLeaseGuard guard,
            ISqlSugarClient leaseDb,
            ISqlSugarClient observerDb,
            RenewalSqlProbe sqlProbe)
        {
            Assignment = assignment;
            Generator = generator;
            _guard = guard;
            _leaseDb = leaseDb;
            ObserverDb = observerDb;
            SqlProbe = sqlProbe;
        }

        public static async Task<LeaseFixture> CreateAsync(
            AdminDatabaseOptions database,
            ControlledTimeProvider clock)
        {
            ISqlSugarClient? leaseDb = null;
            ISqlSugarClient? observerDb = null;
            WorkerIdAssignment? assignment = null;
            WorkerIdLeaseGuard? guard = null;

            try
            {
                leaseDb = OpenClient(database);
                observerDb = OpenClient(database);
                assignment = WorkerIdAssignment.Resolve(
                    new AdminIdOptions { WorkerId = WorkerId });
                var jobs = new AdminJobsOptions
                {
                    HeartbeatSeconds = HeartbeatSeconds,
                    LeaseSeconds = HeartbeatSeconds * 3,
                    SchedulerEnabled = false,
                };
                guard = new WorkerIdLeaseGuard(
                    leaseDb,
                    new AdminIdOptions { WorkerId = WorkerId },
                    jobs,
                    NullLogger<WorkerIdLeaseGuard>.Instance,
                    clock,
                    assignment);

                await guard.StartAsync(CancellationToken.None);
                var probe = new RenewalSqlProbe();
                leaseDb.Aop.OnLogExecuting = probe.OnExecuting;
                leaseDb.Aop.OnLogExecuted = probe.OnExecuted;

                return new LeaseFixture(
                    assignment,
                    SnowflakeIdGenerator.ForAssignment(assignment, clock),
                    guard,
                    leaseDb,
                    observerDb,
                    probe);
            }
            catch
            {
                if (guard is not null)
                {
                    try { guard.Dispose(); }
                    catch { /* 清理不能掩盖初始化异常。 */ }
                }
                try { assignment?.Dispose(); }
                catch { /* 清理不能掩盖初始化异常。 */ }
                leaseDb?.Dispose();
                observerDb?.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await _guard.StopAsync(CancellationToken.None); }
            catch { /* 清理不能掩盖主断言。 */ }
            _guard.Dispose();
            Assignment.Dispose();
            _leaseDb.Dispose();
            ObserverDb.Dispose();
        }
    }

    private sealed class RenewalSqlProbe
    {
        private readonly SemaphoreSlim _started = new(0);
        private readonly SemaphoreSlim _completed = new(0);
        private readonly SemaphoreSlim _ownershipReadCompleted = new(0);

        public void OnExecuting(string sql, SugarParameter[] parameters)
        {
            if (sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("sys_worker_lease", StringComparison.OrdinalIgnoreCase))
            {
                _started.Release();
            }
        }

        public void OnExecuted(string sql, SugarParameter[] parameters)
        {
            if (sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("sys_worker_lease", StringComparison.OrdinalIgnoreCase))
            {
                _completed.Release();
            }
            else if (sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("sys_worker_lease", StringComparison.OrdinalIgnoreCase))
            {
                _ownershipReadCompleted.Release();
            }
        }

        public async Task WaitForNextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _started.WaitAsync(timeout.Token);
        }

        public async Task WaitForCompletionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _completed.WaitAsync(timeout.Token);
        }

        public async Task WaitForOwnershipReadCompletionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _ownershipReadCompleted.WaitAsync(timeout.Token);
        }
    }

    private sealed class InitialLeaseUpdateGate : IDisposable
    {
        private readonly TaskCompletionSource _reached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private int _entered;

        public void OnExecuting(string sql, SugarParameter[] parameters)
        {
            if (!sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !sql.Contains("sys_worker_lease", StringComparison.OrdinalIgnoreCase)
                || Interlocked.Exchange(ref _entered, 1) != 0)
            {
                return;
            }

            _reached.TrySetResult();
            _release.Wait();
        }

        public async Task WaitUntilReachedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _reached.Task.WaitAsync(timeout.Token);
        }

        public void Release() => _release.Set();

        public void Dispose() => _release.Dispose();
    }

    private static ISqlSugarClient OpenClient(AdminDatabaseOptions database)
    {
        if (!Enum.TryParse<DbType>(database.DbType, ignoreCase: true, out var dbType))
            throw new InvalidOperationException($"未知 DbType \"{database.DbType}\"。");

        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConfigId = $"WorkerIdLeaseRecovery-{Guid.NewGuid():N}",
            DbType = dbType,
            ConnectionString = database.ConnectionString,
            IsAutoCloseConnection = true,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        });
        if (dbType == DbType.Sqlite)
        {
            var sqlite = new SqliteConnectionStringBuilder(database.ConnectionString);
            client.Ado.CommandTimeOut = sqlite.DefaultTimeout;
        }
        return client;
    }

    private static ISqlSugarClient OpenPinnedLeaseClient(AdminDatabaseOptions database)
    {
        if (!Enum.TryParse<DbType>(database.DbType, ignoreCase: true, out var dbType))
            throw new InvalidOperationException($"未知 DbType \"{database.DbType}\"。");

        var connectionString = database.ConnectionString;
        if (dbType == DbType.MySql)
        {
            var mysql = new MySqlConnectionStringBuilder(connectionString)
            {
                UseAffectedRows = true,
            };
            connectionString = mysql.ConnectionString;
        }

        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConfigId = $"WorkerIdLeaseFresh-{Guid.NewGuid():N}",
            DbType = dbType,
            ConnectionString = connectionString,
            IsAutoCloseConnection = false,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        });
        if (dbType == DbType.Sqlite)
        {
            var sqlite = new SqliteConnectionStringBuilder(connectionString);
            client.Ado.CommandTimeOut = sqlite.DefaultTimeout;
        }
        client.Ado.Open();
        return client;
    }

    private sealed class LeaseRowBlocker : IAsyncDisposable
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction _transaction;
        private bool _completed;

        private LeaseRowBlocker(DbConnection connection, DbTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public static async Task<LeaseRowBlocker> AcquireAsync(string connectionString)
        {
            DbConnection connection = TestDb.DbType switch
            {
                "MySql" => new MySqlConnection(connectionString),
                "SqlServer" => new SqlConnection(connectionString),
                "PostgreSQL" => new NpgsqlConnection(connectionString),
                _ => new SqliteConnection(connectionString),
            };
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            var blocker = new LeaseRowBlocker(connection, transaction);
            try
            {
                var table = Quote("sys_worker_lease");
                var nodeName = Quote("NodeName");
                var workerId = Quote("WorkerId");
                await blocker.ExecuteAsync(
                    $"UPDATE {table} SET {nodeName} = {nodeName} WHERE {workerId} = @workerId",
                    ("@workerId", WorkerId));
                return blocker;
            }
            catch
            {
                await blocker.DisposeAsync();
                throw;
            }
        }

        public Task ReplaceOwnerAsync(string nodeName, DateTime expiresAt) => ExecuteAsync(
            $"UPDATE {Quote("sys_worker_lease")} SET {Quote("NodeName")} = @nodeName, " +
            $"{Quote("MachineName")} = @machine, {Quote("Pid")} = @pid, " +
            $"{Quote("LeaseExpiresAt")} = @expiresAt WHERE {Quote("WorkerId")} = @workerId",
            ("@nodeName", nodeName),
            ("@machine", "rival-host"),
            ("@pid", -1),
            ("@expiresAt", expiresAt),
            ("@workerId", WorkerId));

        private static string Quote(string identifier) => TestDb.DbType switch
        {
            "MySql" => $"`{identifier}`",
            "SqlServer" => $"[{identifier}]",
            "PostgreSQL" => $"\"{identifier.ToLowerInvariant()}\"",
            _ => $"\"{identifier}\"",
        };

        public async Task CommitAsync()
        {
            if (_completed) return;
            await _transaction.CommitAsync();
            _completed = true;
        }

        private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                try { await _transaction.RollbackAsync(); }
                catch { /* 清理不能掩盖主断言。 */ }
            }
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// 只控制本地单调期限与续租节拍；数据库读写仍使用真实连接和真实锁。
    /// 真实续租 UPDATE 的启动由 SqlSugar AOP 同步，时钟不探测实现内部的 GetTimestamp 调用次数。
    /// </summary>
    private sealed class ControlledTimeProvider : TimeProvider
    {
        private readonly ConcurrentQueue<ControlledTimer> _timers = new();
        private readonly SemaphoreSlim _timerAvailable = new(0);
        private readonly object _probeGate = new();
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_probeGate) return _utcNow;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_probeGate) return _timestamp;
        }

        public void Advance(TimeSpan delta)
        {
            lock (_probeGate)
            {
                _utcNow = _utcNow.Add(delta);
                _timestamp += delta.Ticks;
            }
        }

        public async Task ReleaseNextTimerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                await _timerAvailable.WaitAsync(timeout.Token);
                if (_timers.TryDequeue(out var timer) && timer.Fire())
                    return;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ControlledTimer(callback, state);
            _timers.Enqueue(timer);
            _timerAvailable.Release();
            return timer;
        }

        private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;

            public bool Fire()
            {
                if (Volatile.Read(ref _disposed) != 0) return false;
                ThreadPool.QueueUserWorkItem(_ => callback(state));
                return true;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                Volatile.Read(ref _disposed) == 0;

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ConsumerLifetime : IWorkerIdLeaseLifetime
    {
        public int CheckCount { get; private set; }
        public bool Disposed { get; private set; }

        public void EnsureActive()
        {
            CheckCount++;
            if (Disposed)
                throw new InvalidOperationException("消费者租约已释放。");
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class UnusedTokenProvider : ITokenProvider
    {
        public TokenPair Create(TokenSubject subject) =>
            throw new NotSupportedException("WorkerId 恢复测试不会签发令牌。");
    }
}
