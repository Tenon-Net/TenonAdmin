using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 领取 CAS——防双发的唯一正确性来源(scheduling-ledger §5.2/§12)。
/// <b>全模块最值钱的变异判据:删掉 ClaimAsync 里 <c>NextRunTime == expected</c> 那半句,本类必须红。</b>
/// DateTime 等值 CAS 是 SqlServer 方言敏感面,本类进 backend-ci 的 SqlServer 推送腿子集(G5)。
/// </summary>
public class JobClaimTests : IAsyncLifetime
{
    private readonly string _id = $"jobclaim-{Guid.NewGuid():N}";
    private readonly string _dbFile;
    private readonly MutableTime _clock = new(new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));
    private readonly JobEngineHost _a;
    private readonly JobEngineHost _b;

    public JobClaimTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_id}.db");
        _a = new JobEngineHost(_id, _dbFile, "node-a", _clock);
        _b = new JobEngineHost(_id, _dbFile, "node-b", _clock, workerId: 1);
        _a.InitTables();
    }

    [Fact]
    public async Task Same_occurrence_is_claimed_exactly_once()
    {
        var now = _a.Now;
        var job = await _a.InsertJobAsync("whatever");
        var expected = job.NextRunTime!.Value;
        var next = expected.AddSeconds(5);

        var scheduler = _a.NewScheduler();
        var first = await scheduler.ClaimForTestAsync(job, expected, next, now);
        var second = await scheduler.ClaimForTestAsync(job, expected, next, now);

        Assert.True(first);
        Assert.False(second);   // NextRunTime 已被推进,@expected 对不上——脑裂/GC 停顿的数学保证
        var row = await _a.ReadJobAsync(job.Id);
        Assert.Equal(1, row.NumberOfRuns);
        Assert.Equal(next, row.NextRunTime);
    }

    [Fact]
    public async Task Two_nodes_racing_the_same_occurrence_only_one_wins()
    {
        var now = _a.Now;
        var job = await _a.InsertJobAsync("whatever");
        var expected = job.NextRunTime!.Value;
        var next = expected.AddSeconds(5);

        var claimA = await _a.NewScheduler().ClaimForTestAsync(job, expected, next, now);
        var claimB = await _b.NewScheduler().ClaimForTestAsync(job, expected, next, now);

        Assert.True(claimA ^ claimB);   // 恰一家领走
        Assert.Equal(1, (await _a.ReadJobAsync(job.Id)).NumberOfRuns);
    }

    [Fact]
    public async Task Claim_fails_when_job_paused_or_expected_stale()
    {
        var now = _a.Now;
        var scheduler = _a.NewScheduler();

        var paused = await _a.InsertJobAsync("whatever", j => j.Status = JobStatus.Paused);
        Assert.False(await scheduler.ClaimForTestAsync(paused, paused.NextRunTime!.Value, now.AddSeconds(5), now));

        var job = await _a.InsertJobAsync("whatever");
        Assert.False(await scheduler.ClaimForTestAsync(job, job.NextRunTime!.Value.AddSeconds(1), now.AddSeconds(5), now));
        Assert.Equal(0, (await _a.ReadJobAsync(job.Id)).NumberOfRuns);
    }

    [Fact]
    public void Compute_next_always_lands_on_whole_seconds()
    {
        // 整秒截断纪律(§13-9):MySQL datetime 毫秒四舍五入会让 CAS 无声失效
        var messy = new DateTime(2026, 7, 26, 12, 0, 0).AddMilliseconds(730);
        var interval = new SysJob { TriggerKind = JobTriggerKind.Interval, IntervalSeconds = 5 };
        var cron = new SysJob { TriggerKind = JobTriggerKind.Cron, CronExpression = "*/7 * * * * ?" };
        var oneShot = new SysJob { TriggerKind = JobTriggerKind.OneShot, OneShotTime = messy.AddMinutes(1) };

        foreach (var job in new[] { interval, cron, oneShot })
        {
            var next = JobTrigger.ComputeNext(job, messy);
            Assert.NotNull(next);
            Assert.Equal(0, next!.Value.Millisecond);
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _a.DisposeAsync();
        await _b.DisposeAsync();
        TestDb.Cleanup(_id, _dbFile);
    }
}
