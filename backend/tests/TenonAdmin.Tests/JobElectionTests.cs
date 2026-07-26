using SqlSugar;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 选主协议(scheduling-ledger §5.2):单主、租约过期接管、同名节点重启即刻收回。
/// 租约只管效率;防双发的正确性另由 <see cref="JobClaimTests"/> 锁死。
/// </summary>
public class JobElectionTests : IAsyncLifetime
{
    private readonly string _id = $"jobelect-{Guid.NewGuid():N}";
    private readonly string _dbFile;
    private readonly MutableTime _clock = new(new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));
    private readonly JobEngineHost _a;
    private readonly JobEngineHost _b;

    public JobElectionTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_id}.db");
        _a = new JobEngineHost(_id, _dbFile, "node-a", _clock);
        _b = new JobEngineHost(_id, _dbFile, "node-b", _clock, workerId: 1);
        _a.InitTables();
    }

    [Fact]
    public async Task Only_one_node_becomes_leader()
    {
        var schedulerA = _a.NewScheduler();
        var schedulerB = _b.NewScheduler();
        await schedulerA.TickAsync();
        await schedulerB.TickAsync();

        Assert.True(schedulerA.IsLeader);
        Assert.False(schedulerB.IsLeader);
        var lockRow = await _a.Db.Queryable<SysJobLock>().Where(l => l.Id == SysJobLock.SingletonId).FirstAsync();
        Assert.Equal("node-a", lockRow.OwnerNodeName);
        Assert.Equal(1, lockRow.Term);
        // 两个节点都注册了心跳行
        Assert.Equal(2, await _a.Db.Queryable<SysJobNode>().CountAsync());
    }

    [Fact]
    public async Task Standby_takes_over_after_lease_expires_and_old_leader_steps_down()
    {
        var schedulerA = _a.NewScheduler();
        var schedulerB = _b.NewScheduler();
        await schedulerA.TickAsync();
        await schedulerB.TickAsync();
        Assert.True(schedulerA.IsLeader);

        // 主失联(不再心跳),拨过租约(30s)→ 备节点下一拍夺取,Term+1
        _clock.Advance(TimeSpan.FromSeconds(40));
        await schedulerB.TickAsync();
        Assert.True(schedulerB.IsLeader);
        var lockRow = await _b.Db.Queryable<SysJobLock>().Where(l => l.Id == SysJobLock.SingletonId).FirstAsync();
        Assert.Equal("node-b", lockRow.OwnerNodeName);
        Assert.Equal(2, lockRow.Term);

        // 旧主醒来续租失败 → 立刻自认失主
        await schedulerA.TickAsync();
        Assert.False(schedulerA.IsLeader);
    }

    [Fact]
    public async Task Restarted_leader_with_same_node_name_reclaims_without_waiting_lease()
    {
        var schedulerA = _a.NewScheduler();
        await schedulerA.TickAsync();
        Assert.True(schedulerA.IsLeader);

        // 同名"新进程"(重启):租约未过期,靠 OwnerNodeName==me 分支即刻收回
        _clock.Advance(TimeSpan.FromSeconds(5));
        var restarted = _a.NewScheduler();
        await restarted.TickAsync();
        Assert.True(restarted.IsLeader);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _a.DisposeAsync();
        await _b.DisposeAsync();
        TestDb.Cleanup(_id, _dbFile);
    }
}
