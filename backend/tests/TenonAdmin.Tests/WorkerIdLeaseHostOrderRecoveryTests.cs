using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Npgsql;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// Host 的 Starting 阶段先建立租约；数据库 bootstrap 长时间占锁后，Guard 必须在调度器之前
/// 做一次新鲜确认，后置业务才能安全生成 ID。
/// </summary>
public class WorkerIdLeaseHostOrderRecoveryTests
{
    private const int WorkerId = 7;
    private const int HeartbeatSeconds = 2;
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(HeartbeatSeconds * 3);

    [Fact]
    public async Task Guard_freshly_confirms_after_locked_bootstrap_before_scheduler_and_business_start()
    {
        var identity = $"wid-host-recover-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{identity}.db");
        var database = DatabaseOptions(identity, dbFile);
        IHost? host = null;

        try
        {
            var blocker = new LockedBootstrapHostedService(database.ConnectionString);
            host = BuildHost(database, blocker);

            await host.StartAsync();

            var probe = host.Services.GetRequiredService<IdGeneratingHostedService>();
            Assert.True(blocker.LockedExistingLeaseRow);
            Assert.True(blocker.HeldFor >= LeaseTtl);
            Assert.True(probe.GeneratedId > 0);
            Assert.True(host.Services.GetRequiredService<IIdGenerator>().NextId() > 0);

            // 先证明真实启动行为，再检查运行时类型顺序，避免旧实现只因注册位置而提前红。
            var hosted = host.Services.GetServices<IHostedService>().ToList();
            Assert.True(IndexOfTypeName(hosted, "DatabaseInitializer") < IndexOf<WorkerIdLeaseGuard>(hosted));
            Assert.True(IndexOf<WorkerIdLeaseGuard>(hosted) < IndexOf<JobSchedulerService>(hosted));
            Assert.True(IndexOf<JobSchedulerService>(hosted) < hosted.IndexOf(probe));
        }
        finally
        {
            if (host is not null)
            {
                try { await host.StopAsync(); }
                catch { /* 清理不覆盖主断言。 */ }
                host.Dispose();
            }
            TestDb.Cleanup(identity, dbFile);
        }
    }

    private static IHost BuildHost(
        AdminDatabaseOptions database,
        LockedBootstrapHostedService blocker)
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
        builder.Services.AddSingleton(blocker);
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LockedBootstrapHostedService>());
        builder.Services.AddTenonAdminWorker(builder.Configuration);
        builder.Services.AddSingleton<IdGeneratingHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IdGeneratingHostedService>());
        return builder.Build();
    }

    private static int IndexOf<T>(IReadOnlyList<IHostedService> services) where T : IHostedService
    {
        var index = services.ToList().FindIndex(service => service is T);
        Assert.True(index >= 0, $"未注册 {typeof(T).Name}。");
        return index;
    }

    private static int IndexOfTypeName(IReadOnlyList<IHostedService> services, string typeName)
    {
        var index = services.ToList().FindIndex(service => service.GetType().Name == typeName);
        Assert.True(index >= 0, $"未注册 {typeName}。");
        return index;
    }

    private static AdminDatabaseOptions DatabaseOptions(string identity, string dbFile) => new()
    {
        DbType = TestDb.DbType,
        ConnectionString = TestDb.ConnectionString(identity, dbFile),
        EnableCodeFirst = true,
        EnableSeed = false,
    };

    private sealed class LockedBootstrapHostedService(string connectionString) : IHostedService
    {
        public bool LockedExistingLeaseRow { get; private set; }
        public TimeSpan HeldFor { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var connection = CreateConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"UPDATE {Quote("sys_worker_lease")} SET {Quote("NodeName")} = {Quote("NodeName")} " +
                $"WHERE {Quote("WorkerId")} = @workerId";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@workerId";
            parameter.Value = WorkerId;
            command.Parameters.Add(parameter);
            LockedExistingLeaseRow = await command.ExecuteNonQueryAsync(cancellationToken) == 1;

            var started = Stopwatch.GetTimestamp();
            await Task.Delay(LeaseTtl + TimeSpan.FromSeconds(1), cancellationToken);
            HeldFor = Stopwatch.GetElapsedTime(started);
            await transaction.CommitAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class IdGeneratingHostedService(IIdGenerator generator) : IHostedService
    {
        public long GeneratedId { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            GeneratedId = generator.NextId();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static DbConnection CreateConnection(string connectionString) => TestDb.DbType switch
    {
        "MySql" => new MySqlConnection(connectionString),
        "SqlServer" => new SqlConnection(connectionString),
        "PostgreSQL" => new NpgsqlConnection(connectionString),
        _ => new SqliteConnection(connectionString),
    };

    private static string Quote(string identifier) => TestDb.DbType switch
    {
        "MySql" => $"`{identifier}`",
        "SqlServer" => $"[{identifier}]",
        "PostgreSQL" => $"\"{identifier.ToLowerInvariant()}\"",
        _ => $"\"{identifier}\"",
    };

    private sealed class UnusedTokenProvider : ITokenProvider
    {
        public TokenPair Create(TokenSubject subject) =>
            throw new NotSupportedException("WorkerId Host 顺序测试不会签发令牌。");
    }
}
