using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 定时任务引擎测试宿主(FileGcTests 同款裸容器成法):共享 TestDb、可拨时钟、拍子手动推
/// (直接调 <see cref="JobSchedulerService.TickAsync"/>,不跑真循环、不启 hosted service)。
/// 多节点场景 = 同一 identity 建多个宿主(共库),各配 NodeName。
/// </summary>
internal sealed class JobEngineHost : IAsyncDisposable
{
    public MutableTime Clock { get; }
    public ServiceProvider Sp { get; }
    public AdminJobsOptions Jobs { get; }

    public ISqlSugarClient Db => Sp.GetRequiredService<ISqlSugarClient>();
    public JobExecutor Executor => Sp.GetRequiredService<JobExecutor>();

    public JobEngineHost(
        string identity,
        string dbFile,
        string nodeName,
        MutableTime clock,
        Action<IServiceCollection>? configure = null,
        Action<AdminJobsOptions>? tuneJobs = null,
        int workerId = 0)   // 多宿主共库必须互异——同号同毫秒雪花撞主键(§13-5 的实证)
    {
        Clock = clock;
        Jobs = new AdminJobsOptions { NodeName = nodeName, KillPollSeconds = 1 };
        tuneJobs?.Invoke(Jobs);
        var dbOptions = new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(identity, dbFile) };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(new AdminIdOptions { WorkerId = workerId });
        services.AddSingleton(Jobs);
        services.AddSingleton(dbOptions);
        services.AddSingleton<TimeProvider>(clock);
        configure?.Invoke(services);   // 前置注册(假 IEmailSender / 测试处理器)压过内核 TryAdd
        services.AddTenonAdminSqlSugar(dbOptions, [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        Sp = services.BuildServiceProvider();
    }

    /// <summary>建本模块所需的表(含告警要写的通知两张)。多宿主共库时只需在第一个宿主上调。</summary>
    public void InitTables()
    {
        Db.CodeFirst.InitTables(
            typeof(SysJob), typeof(SysJobLog), typeof(SysJobLock), typeof(SysJobNode),
            typeof(SysConfig), typeof(SysNotice), typeof(SysNoticeReceiver), typeof(SysUser));

        // Panic 站内信定向到超管 Id=1(QA25 校验目标必须存在);精简宿主不跑种子,这里补一行。
        const long superAdminId = 1; // = SuperAdminSeed.SUPER_ADMIN_ID (internal to Services)
        if (!Db.Queryable<SysUser>().ClearFilter<ISoftDelete>().Any(u => u.Id == superAdminId))
        {
            Db.Insertable(new SysUser
            {
                Id = superAdminId,
                Account = "superAdmin",
                Name = "超管",
                Password = "x",
                Enabled = true,
                IsSuperAdmin = true,
            }).ExecuteCommand();
        }
    }

    public TestJobScheduler NewScheduler() => ActivatorUtilities.CreateInstance<TestJobScheduler>(Sp);

    /// <summary>插入一行任务(默认:编译类 + 5s 间隔 + Ready,NextRunTime = 当前时钟整秒)。</summary>
    public async Task<SysJob> InsertJobAsync(string handlerName, Action<SysJob>? mutate = null)
    {
        var now = Now;
        var job = new SysJob
        {
            Code = $"t-{Guid.NewGuid():N}"[..14],
            Name = "测试任务",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = handlerName,
            TriggerKind = JobTriggerKind.Interval,
            IntervalSeconds = 5,
            Status = JobStatus.Ready,
            NextRunTime = now,
        };
        mutate?.Invoke(job);
        await Db.Insertable(job).ExecuteCommandAsync();   // AOP 填雪花 Id/CreateTime
        return job;
    }

    /// <summary>当前时钟(本地=UTC,整秒)。</summary>
    public DateTime Now => new(Clock.GetUtcNow().Year, Clock.GetUtcNow().Month, Clock.GetUtcNow().Day,
        Clock.GetUtcNow().Hour, Clock.GetUtcNow().Minute, Clock.GetUtcNow().Second);

    public Task<SysJob> ReadJobAsync(long id) =>
        Db.Queryable<SysJob>().ClearFilter<ISoftDelete>().Where(j => j.Id == id).FirstAsync();

    public Task<List<SysJobLog>> ReadLogsAsync(long jobId) =>
        Db.Queryable<SysJobLog>().Where(l => l.JobId == jobId).OrderBy(l => l.Id).ToListAsync();

    /// <summary>真实等待(执行器 fire-and-forget 用):直到出现满足条件的执行记录或超时。</summary>
    public async Task<List<SysJobLog>> WaitForLogsAsync(long jobId, Func<List<SysJobLog>, bool> ready, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var logs = await ReadLogsAsync(jobId);
            if (ready(logs)) return logs;
            if (Environment.TickCount64 > deadline) return logs;
            await Task.Delay(100);
        }
    }

    public async ValueTask DisposeAsync() => await Sp.DisposeAsync();
}

/// <summary>可拨时钟(本地时区固定 UTC,免宿主机时区影响向量)。</summary>
internal sealed class MutableTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>暴露 protected 步骤(领取 CAS)+ 顺带证明子类可继承(六件套姿势)。</summary>
internal sealed class TestJobScheduler(
    ISqlSugarClient db,
    JobExecutor executor,
    IEventBus eventBus,
    AdminJobsOptions options,
    AdminIdOptions idOptions,
    AdminDatabaseOptions dbOptions,
    IIdGenerator idGenerator,
    TimeProvider time,
    ILogger<JobSchedulerService> logger)
    : JobSchedulerService(db, executor, eventBus, options, idOptions, dbOptions, idGenerator, time, logger)
{
    public Task<bool> ClaimForTestAsync(SysJob job, DateTime expected, DateTime? next, DateTime now)
        => ClaimAsync(job, expected, next, now);
}
