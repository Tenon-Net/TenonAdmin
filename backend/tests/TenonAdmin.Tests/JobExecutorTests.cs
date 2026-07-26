using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 执行器(scheduling-ledger §5.4):重试共享 FireInstanceId、超时/取消不重试、47005 失败行、
/// 连败 Panic 且告警只发跨阈那一次、OneShot 成功即完结、本机 kill。
/// </summary>
public class JobExecutorTests : IAsyncLifetime
{
    private readonly string _id = $"jobexec-{Guid.NewGuid():N}";
    private readonly string _dbFile;
    private readonly MutableTime _clock = new(new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));
    private readonly JobEngineHost _host;
    private readonly CapturingEmailSender _email = new();

    public JobExecutorTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_id}.db");
        _host = new JobEngineHost(_id, _dbFile, "node-a", _clock, configure: services =>
        {
            services.AddSingleton<IEmailSender>(_email);   // 前置注册压过内核默认(六件套姿势)
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, FlakyJob>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, SlowJob>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, AlwaysFailJob>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, OkJob>());
        });
        _host.InitTables();
        FlakyJob.RemainingFailures = 2;
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public readonly List<string> Sent = [];
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            lock (Sent) Sent.Add(to);
            return Task.CompletedTask;
        }
    }

    private sealed class OkJob : IAdminJob
    {
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FlakyJob : IAdminJob
    {
        public static int RemainingFailures = 2;   // 类内测试串行(xUnit 同类不并行),静态计数安全
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref RemainingFailures) >= 0)
                throw new InvalidOperationException($"故意失败(第 {context.RetryIndex} 次尝试)");
            return Task.CompletedTask;
        }
    }

    private sealed class SlowJob : IAdminJob
    {
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
            => Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
    }

    private sealed class AlwaysFailJob : IAdminJob
    {
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("永远失败");
    }

    [Fact]
    public async Task Retries_share_fire_instance_and_success_resets_consecutive_errors()
    {
        FlakyJob.RemainingFailures = 2;
        var job = await _host.InsertJobAsync(typeof(FlakyJob).FullName!, j =>
        {
            j.RetryCount = 2;
            j.RetryIntervalSeconds = 0;
            j.ConsecutiveErrors = 5;   // 成功必须清零
        });
        await _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Schedule);

        var logs = await _host.ReadLogsAsync(job.Id);
        Assert.Equal(3, logs.Count);
        Assert.Single(logs.Select(l => l.FireInstanceId).Distinct());
        Assert.Equal([0, 1, 2], logs.Select(l => l.RetryIndex).ToArray());
        Assert.Equal([JobRunStatus.Failed, JobRunStatus.Failed, JobRunStatus.Success], logs.Select(l => l.RunStatus).ToArray());
        Assert.All(logs, l => Assert.NotNull(l.EndTime));
        Assert.Equal(0, (await _host.ReadJobAsync(job.Id)).ConsecutiveErrors);
    }

    [Fact]
    public async Task Timeout_closes_as_timeout_and_does_not_retry()
    {
        var job = await _host.InsertJobAsync(typeof(SlowJob).FullName!, j =>
        {
            j.TimeoutSeconds = 1;
            j.RetryCount = 3;   // 取消不重试:仍应只有一行
        });
        await _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Schedule);

        var log = Assert.Single(await _host.ReadLogsAsync(job.Id));
        Assert.Equal(JobRunStatus.Timeout, log.RunStatus);
        var row = await _host.ReadJobAsync(job.Id);
        Assert.Equal(1, row.NumberOfErrors);   // 超时计失败
        Assert.Equal(1, row.ConsecutiveErrors);
    }

    [Fact]
    public async Task Unregistered_handler_fails_loudly_without_retry()
    {
        var job = await _host.InsertJobAsync("No.Such.Handler", j => j.RetryCount = 2);
        await _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Schedule);

        var log = Assert.Single(await _host.ReadLogsAsync(job.Id));
        Assert.Equal(JobRunStatus.Failed, log.RunStatus);
        Assert.Contains("47005", log.ErrorText);
        Assert.Contains("No.Such.Handler", log.ErrorText);
    }

    [Fact]
    public async Task Panic_fires_alert_exactly_once_at_threshold()
    {
        var job = await _host.InsertJobAsync(typeof(AlwaysFailJob).FullName!, j =>
        {
            j.FailAlertThreshold = 2;
            j.AlertByNotice = true;
            j.AlertEmails = "ops@test.local";
        });

        await _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Schedule);
        var afterFirst = await _host.ReadJobAsync(job.Id);
        Assert.Equal(JobStatus.Ready, afterFirst.Status);
        Assert.Equal(1, afterFirst.ConsecutiveErrors);
        Assert.Empty(_email.Sent);

        await _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Schedule);
        var afterSecond = await _host.ReadJobAsync(job.Id);
        Assert.Equal(JobStatus.Panic, afterSecond.Status);
        Assert.Null(afterSecond.NextRunTime);
        Assert.Equal(["ops@test.local"], _email.Sent);
        var notices = await _host.Db.Queryable<SysNotice>().ToListAsync();
        var notice = Assert.Single(notices);
        Assert.Contains(job.Name, notice.Title);
        // 定向超管,不广播
        Assert.Equal(ReceiverType.User, notice.ReceiverType);

        // 第三次失败:已是 Panic,Ready→Panic 的 CAS 不再成功 → 不重复告警
        await _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Schedule);
        Assert.Single(_email.Sent);
        Assert.Single(await _host.Db.Queryable<SysNotice>().ToListAsync());
    }

    [Fact]
    public async Task One_shot_success_marks_job_completed()
    {
        var job = await _host.InsertJobAsync(typeof(OkJob).FullName!, j =>
        {
            j.TriggerKind = JobTriggerKind.OneShot;
            j.IntervalSeconds = null;
            j.OneShotTime = _host.Now;
        });
        await _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Schedule);
        Assert.Equal(JobStatus.Completed, (await _host.ReadJobAsync(job.Id)).Status);
    }

    [Fact]
    public async Task Local_kill_cancels_run_without_counting_failure()
    {
        var job = await _host.InsertJobAsync(typeof(SlowJob).FullName!);
        var fire = _host.Executor.FireAndTrack(job, _host.Now, JobFireMode.Manual);
        var logs = await _host.WaitForLogsAsync(job.Id, l => l.Count == 1);
        var running = Assert.Single(logs);

        Assert.True(_host.Executor.TryCancelLocal(running.Id));
        await fire;

        var closed = Assert.Single(await _host.ReadLogsAsync(job.Id));
        Assert.Equal(JobRunStatus.Cancelled, closed.RunStatus);
        var row = await _host.ReadJobAsync(job.Id);
        Assert.Equal(0, row.NumberOfErrors);      // 取消不计失败
        Assert.Equal(1, row.NumberOfRuns);        // Manual 触发在收尾处补计数
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        TestDb.Cleanup(_id, _dbFile);
    }
}
