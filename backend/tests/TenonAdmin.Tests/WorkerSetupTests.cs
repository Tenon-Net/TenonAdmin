using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 独立 Worker 的组合根(scheduling-ledger §10.2):装配得出调度器、WorkerId 守卫、租约参数守卫。
/// Worker 是「API 停了任务照跑」的官方配方,消费者默认用不到——但一旦用,它就是多实例形态,
/// 机器号同号会在同毫秒撞主键,所以守卫比 API 侧更严(API 可能真是单实例,Worker 不可能)。
/// </summary>
public class WorkerSetupTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Baseline(string dbFile) => new()
    {
        ["TenonAdmin:Database:DbType"] = "Sqlite",
        ["TenonAdmin:Database:ConnectionString"] = $"Data Source={dbFile}",
        ["TenonAdmin:Database:EnableCodeFirst"] = "false",   // schema 归 API 侧所有
        ["TenonAdmin:Database:EnableSeed"] = "false",
        ["TenonAdmin:Id:WorkerId"] = "7",
    };

    /// <summary>照 samples/WorkerHost 的真实姿势装配:Generic Host + 一行 AddTenonAdminWorker。</summary>
    [Fact]
    public void Worker_host_resolves_the_scheduler_and_its_dependencies()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-worker-{Guid.NewGuid():N}.db");
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Baseline(dbFile));
        builder.Services.AddTenonAdminWorker(builder.Configuration);

        using var host = builder.Build();
        var scheduler = host.Services.GetRequiredService<JobSchedulerService>();
        Assert.NotNull(host.Services.GetRequiredService<JobExecutor>());
        Assert.NotNull(host.Services.GetRequiredService<IJobHandlerResolver>());
        Assert.Equal($"{Environment.MachineName}#7", scheduler.NodeName);
        // 双注册:同一实例既可解析也被托管(不会被实例化两份)
        Assert.Contains(host.Services.GetServices<IHostedService>(), s => ReferenceEquals(s, scheduler));
        // 三个内置处理器随 Services 层一起进来了(Worker 不装 AspNetCore 也照样跑任务)
        using var scope = host.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAdminJob>().Select(h => h.Name).ToList();
        Assert.Contains(typeof(JobLogCleanupJob).FullName, handlers);

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public void Worker_without_explicit_worker_id_throws()
    {
        var settings = Baseline("ignored.db");
        settings.Remove("TenonAdmin:Id:WorkerId");
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTenonAdminWorker(Config(settings)));
        Assert.Contains("WorkerId", ex.Message);
    }

    [Fact]
    public void Worker_with_lease_not_covering_two_heartbeats_throws()
    {
        var settings = Baseline("ignored.db");
        settings["TenonAdmin:Jobs:HeartbeatSeconds"] = "10";
        settings["TenonAdmin:Jobs:LeaseSeconds"] = "20";   // 必须 > 2×心跳,否则一次 GC 停顿就丢主、主备震荡
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTenonAdminWorker(Config(settings)));
        Assert.Contains("LeaseSeconds", ex.Message);
    }

    [Fact]
    public void Worker_with_invalid_blocked_cidr_throws()
    {
        // Worker 与 API 共用校验:非法 CIDR 必须启动即拒,不能等到执行 HTTP 任务时静默失效
        var settings = Baseline("ignored.db");
        settings["TenonAdmin:Jobs:Http:BlockedCidrs:0"] = "not-a-cidr";
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTenonAdminWorker(Config(settings)));
        Assert.Contains("BlockedCidrs", ex.Message);
        Assert.Contains("not-a-cidr", ex.Message);
    }

    [Theory]
    [InlineData("HeartbeatSeconds", "0")]
    [InlineData("ReloadSeconds", "0")]
    [InlineData("MisfireThresholdSeconds", "0")]
    [InlineData("MaxConcurrentRuns", "0")]
    public void Worker_with_non_positive_job_option_throws(string key, string value)
    {
        var settings = Baseline("ignored.db");
        settings[$"TenonAdmin:Jobs:{key}"] = value;
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTenonAdminWorker(Config(settings)));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Worker_with_negative_max_response_log_bytes_throws()
    {
        var settings = Baseline("ignored.db");
        settings["TenonAdmin:Jobs:Http:MaxResponseLogBytes"] = "-1";
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTenonAdminWorker(Config(settings)));
        Assert.Contains("MaxResponseLogBytes", ex.Message);
    }

    [Fact]
    public void Shared_validation_rejects_invalid_cidr_for_api_and_worker_alike()
    {
        var jobs = new AdminJobsOptions { Http = { BlockedCidrs = ["169.254.0.0/16", "bogus"] } };
        var ex = Assert.Throws<InvalidOperationException>(() => AdminJobsOptionsValidation.Validate(jobs));
        Assert.Contains("BlockedCidrs", ex.Message);
    }

    /// <summary>
    /// Worker 实体扫描必须含 Services 程序集(与 HTTP 组合根对称)。默认关 CodeFirst 时不易暴露,
    /// 但登记表一旦漏挂,运维打开 EnableCodeFirst 只会建出 schema_version、任务表全无。
    /// </summary>
    [Fact]
    public void Worker_entity_scan_includes_services_assembly()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-worker-{Guid.NewGuid():N}.db");
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Baseline(dbFile));
        builder.Services.AddTenonAdminWorker(builder.Configuration);

        using var host = builder.Build();
        var sources = host.Services.GetRequiredService<TenonEntitySources>();
        Assert.Contains(typeof(SysJob).Assembly, sources.Assemblies);

        TestDb.Cleanup(dbFile, dbFile);
    }
}
