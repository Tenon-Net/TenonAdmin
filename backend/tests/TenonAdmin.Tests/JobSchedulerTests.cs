using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 调度循环行为(scheduling-ledger §2.3/§5.3):空 NextRunTime 补算、无未来时刻判死、Paused 不调度、
/// 到期触发并推进、misfire 两策略、SerialSkip/Parallel。拍子手动推(TickAsync),执行结果真实落库后轮询断言。
/// </summary>
public class JobSchedulerTests : IAsyncLifetime
{
    private readonly string _id = $"jobsched-{Guid.NewGuid():N}";
    private readonly string _dbFile;
    private readonly MutableTime _clock = new(new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));
    private readonly JobEngineHost _host;

    private static readonly string OkName = typeof(OkJob).FullName!;

    public JobSchedulerTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_id}.db");
        _host = new JobEngineHost(_id, _dbFile, "node-a", _clock,
            configure: s => s.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, OkJob>()));
        _host.InitTables();
    }

    private sealed class OkJob : IAdminJob
    {
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
        {
            context.Log?.Invoke("ok");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Ready_row_with_null_next_run_time_is_backfilled()
    {
        // 种子行走的就是这条路(DefaultJobSeed 的 NextRunTime 留空)
        var job = await _host.InsertJobAsync(OkName, j =>
        {
            j.TriggerKind = JobTriggerKind.Cron;
            j.CronExpression = "0 30 3 * * ?";
            j.IntervalSeconds = null;
            j.NextRunTime = null;
        });
        await _host.NewScheduler().TickAsync();
        var row = await _host.ReadJobAsync(job.Id);
        Assert.Equal(_host.Now.Date.AddHours(3).AddMinutes(30), row.NextRunTime);
    }

    [Fact]
    public async Task Ready_row_with_no_future_time_is_marked_completed()
    {
        var job = await _host.InsertJobAsync(OkName, j =>
        {
            j.TriggerKind = JobTriggerKind.OneShot;
            j.IntervalSeconds = null;
            j.OneShotTime = _host.Now.AddMinutes(-10);   // 已过去
            j.NextRunTime = null;
        });
        await _host.NewScheduler().TickAsync();
        Assert.Equal(JobStatus.Completed, (await _host.ReadJobAsync(job.Id)).Status);
    }

    [Fact]
    public async Task Paused_job_is_never_dispatched()
    {
        var job = await _host.InsertJobAsync(OkName, j => j.Status = JobStatus.Paused);
        await _host.NewScheduler().TickAsync();
        Assert.Empty(await _host.ReadLogsAsync(job.Id));
        Assert.Equal(job.NextRunTime, (await _host.ReadJobAsync(job.Id)).NextRunTime);
    }

    [Fact]
    public async Task Due_job_fires_and_next_run_time_advances()
    {
        var job = await _host.InsertJobAsync(OkName);
        await _host.NewScheduler().TickAsync();
        var logs = await _host.WaitForLogsAsync(job.Id, l => l.Any(x => x.RunStatus == JobRunStatus.Success));
        var done = Assert.Single(logs);
        Assert.Equal(JobRunStatus.Success, done.RunStatus);
        Assert.Equal(JobFireMode.Schedule, done.FireMode);
        Assert.Contains("ok", done.MessageText);

        var row = await _host.ReadJobAsync(job.Id);
        Assert.Equal(1, row.NumberOfRuns);
        Assert.True(row.NextRunTime > _host.Now);
    }

    [Fact]
    public async Task Misfire_skip_records_single_row_and_advances()
    {
        var expected = _host.Now.AddMinutes(-10);   // 迟到 10 分钟(阈值 60s)
        var job = await _host.InsertJobAsync(OkName, j => j.NextRunTime = expected);
        await _host.NewScheduler().TickAsync();

        var log = Assert.Single(await _host.ReadLogsAsync(job.Id));
        Assert.Equal(JobRunStatus.Skipped, log.RunStatus);
        Assert.Equal(JobFireMode.MissedSkipped, log.FireMode);
        Assert.Equal(expected, log.ScheduledTime);
        Assert.Contains("121", log.MessageText);   // 600s/5s + 1 = 121 次,合并记一行不刷表

        var row = await _host.ReadJobAsync(job.Id);
        Assert.True(row.NextRunTime > _host.Now);
    }

    [Fact]
    public async Task Misfire_fire_once_now_compensates_exactly_once()
    {
        var expected = _host.Now.AddMinutes(-10);
        var job = await _host.InsertJobAsync(OkName, j =>
        {
            j.NextRunTime = expected;
            j.MisfireStrategy = JobMisfireStrategy.FireOnceNow;
        });
        await _host.NewScheduler().TickAsync();

        var logs = await _host.WaitForLogsAsync(job.Id, l => l.Any(x => x.RunStatus == JobRunStatus.Success));
        var done = Assert.Single(logs);   // 错过 121 次也只补一次
        Assert.Equal(JobFireMode.Misfire, done.FireMode);
        Assert.Equal(expected, done.ScheduledTime);
    }

    [Fact]
    public async Task Serial_skip_skips_while_previous_run_is_open()
    {
        var job = await _host.InsertJobAsync(OkName);
        // 手工放一行未闭合记录 = 上次触发还在跑
        await _host.Db.Insertable(new SysJobLog
        {
            JobId = job.Id,
            JobName = job.Name,
            FireInstanceId = 1,
            FireMode = JobFireMode.Schedule,
            ScheduledTime = _host.Now.AddSeconds(-5),
            StartTime = _host.Now.AddSeconds(-5),
            RunStatus = JobRunStatus.Running,
            NodeName = "node-x",
        }).ExecuteCommandAsync();

        await _host.NewScheduler().TickAsync();
        var logs = await _host.ReadLogsAsync(job.Id);
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.RunStatus == JobRunStatus.Skipped && l.EndTime != null);
        Assert.DoesNotContain(logs, l => l.RunStatus == JobRunStatus.Success);
    }

    [Fact]
    public async Task Parallel_mode_fires_even_with_open_run()
    {
        var job = await _host.InsertJobAsync(OkName, j => j.ConcurrencyMode = JobConcurrencyMode.Parallel);
        await _host.Db.Insertable(new SysJobLog
        {
            JobId = job.Id,
            JobName = job.Name,
            FireInstanceId = 1,
            FireMode = JobFireMode.Schedule,
            ScheduledTime = _host.Now.AddSeconds(-5),
            StartTime = _host.Now.AddSeconds(-5),
            RunStatus = JobRunStatus.Running,
            NodeName = "node-x",
        }).ExecuteCommandAsync();

        await _host.NewScheduler().TickAsync();
        var logs = await _host.WaitForLogsAsync(job.Id, l => l.Any(x => x.RunStatus == JobRunStatus.Success));
        Assert.Contains(logs, l => l.RunStatus == JobRunStatus.Success);
    }

    [Fact]
    public async Task Orphan_running_rows_from_dead_nodes_are_reaped()
    {
        // 崩溃(kill -9)遗留的未闭合行永远闭合不了,而它正是 SerialSkip 的调度输入:
        // 不回收 = 该任务从此每次都被判「上次还在跑」,永久停摆且无 API 级恢复路径。
        var job = await _host.InsertJobAsync(OkName);
        await _host.Db.Insertable(new SysJobLog
        {
            JobId = job.Id,
            JobName = job.Name,
            FireInstanceId = 42,
            FireMode = JobFireMode.Schedule,
            ScheduledTime = _host.Now.AddMinutes(-30),
            StartTime = _host.Now.AddMinutes(-30),
            RunStatus = JobRunStatus.Running,
            NodeName = "node-crashed",          // 从未心跳过 = 陈死节点
        }).ExecuteCommandAsync();

        var scheduler = _host.NewScheduler();
        await scheduler.TickAsync();

        var reaped = (await _host.ReadLogsAsync(job.Id)).Single(l => l.FireInstanceId == 42);
        Assert.Equal(JobRunStatus.Cancelled, reaped.RunStatus);
        Assert.NotNull(reaped.EndTime);
        Assert.Contains("失联", reaped.ErrorText);

        // 回收后任务恢复正常调度(不再被 SerialSkip 判为在跑)
        var logs = await _host.WaitForLogsAsync(job.Id, l => l.Any(x => x.RunStatus == JobRunStatus.Success));
        Assert.Contains(logs, l => l.RunStatus == JobRunStatus.Success);
    }

    [Fact]
    public async Task Live_node_running_rows_are_never_reaped()
    {
        var job = await _host.InsertJobAsync(OkName, j => j.NextRunTime = _host.Now.AddHours(1));
        var scheduler = _host.NewScheduler();
        await scheduler.TickAsync();   // 先让本节点心跳一次

        await _host.Db.Insertable(new SysJobLog
        {
            JobId = job.Id,
            JobName = job.Name,
            FireInstanceId = 43,
            FireMode = JobFireMode.Schedule,
            ScheduledTime = _host.Now.AddMinutes(-30),
            StartTime = _host.Now.AddMinutes(-30),
            RunStatus = JobRunStatus.Running,
            NodeName = scheduler.NodeName,   // 本节点,心跳新鲜
        }).ExecuteCommandAsync();

        await scheduler.TickAsync();
        var row = (await _host.ReadLogsAsync(job.Id)).Single(l => l.FireInstanceId == 43);
        Assert.Equal(JobRunStatus.Running, row.RunStatus);
        Assert.Null(row.EndTime);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        TestDb.Cleanup(_id, _dbFile);
    }
}
