using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 调度循环(scheduling-ledger §5):心跳/选主 + 主节点扫表派发,deadline 睡眠(Furion 思路)。
/// <para><b>正确性分工</b>(§5.2):<c>sys_job_lock</c> 租约只回答「谁来扫表」(效率);
/// 防双发的唯一正确性来源是每次触发对 <c>NextRunTime</c> 的领取 CAS(<see cref="ClaimAsync"/>)——
/// 旧主 GC 停顿醒来后照旧扫表,但那格 NextRunTime 已被推进,CAS 对不上,数学上同一时刻至多一发。</para>
/// <para>唤醒三通道(§5.3):本进程改任务 → <see cref="JobChangedEvent"/> 即刻唤醒;别的副本改任务 →
/// ReloadSeconds 周期重载兜底(事件总线仅进程内);否则心跳节拍(HeartbeatSeconds)是睡眠上限。</para>
/// <para>步骤全 <c>protected virtual</c>;<see cref="TickAsync"/> public——测试直接推拍,不跑真循环。
/// <b>可订阅类型</b>(dev-plan §2.5):新增构造参数一律给默认值。</para>
/// </summary>
public class JobSchedulerService(
    ISqlSugarClient db,
    JobExecutor executor,
    IEventBus eventBus,
    AdminJobsOptions options,
    AdminIdOptions idOptions,
    AdminDatabaseOptions dbOptions,
    IIdGenerator idGenerator,
    TimeProvider time,
    ILogger<JobSchedulerService> logger) : BackgroundService
{
    private readonly Lock _wakeLock = new();
    private CancellationTokenSource? _wake;
    private volatile bool _dirty = true;
    private bool _initialized;
    private volatile bool _isLeader;
    private DateTime _lastReload = DateTime.MinValue;
    private DateTime? _nodeStartTime;
    private List<SysJob> _cache = [];

    /// <summary>本节点名(集群内唯一;默认 <c>{MachineName}#{WorkerId}</c>)</summary>
    public string NodeName { get; } = JobTime.ResolveNodeName(options, idOptions);

    /// <summary>当前是否主节点(监控/测试用;正确性不依赖它)</summary>
    public bool IsLeader => _isLeader;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.SchedulerEnabled)
        {
            logger.LogInformation("TenonAdmin 调度器:本副本不参与调度(Jobs:SchedulerEnabled=false);执行一次/查询/编辑照常。");
            return;
        }
        using var subscription = eventBus.Subscribe<JobChangedEvent>((_, _) =>
        {
            _dirty = true;
            WakeUp();
            return Task.CompletedTask;
        });
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime? nextDue = null;
            try
            {
                nextDue = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 一拍失败(建表未完/DB 抖动)绝不掀翻宿主:记警告,下拍再来。
                logger.LogWarning(ex, "TenonAdmin 调度器:本拍失败,下拍重试。");
            }
            await SleepUntilNextBeatAsync(nextDue, stoppingToken);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);        // 循环退出 = 停发新触发
        await executor.DrainAsync(cancellationToken);   // 宽限期内 drain,逾期逐个 Cancel(§5.3)
    }

    /// <summary>一拍:心跳/选主 + (主节点)重载与派发。返回缓存中最近到期时刻(睡眠上限)。</summary>
    public virtual async Task<DateTime?> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = JobTime.Truncate(time.GetLocalNow().DateTime);
        await EnsureInitializedAsync(now, cancellationToken);
        await HeartbeatAsync(now, cancellationToken);
        if (!_isLeader) return null;
        await ReapOrphanRunsAsync(now, cancellationToken);
        if (_dirty || now >= _lastReload.AddSeconds(options.ReloadSeconds))
            await ReloadJobsAsync(now, cancellationToken);
        await DispatchDueJobsAsync(now, cancellationToken);
        return _cache.Where(j => j.NextRunTime is not null).Select(j => j.NextRunTime).Min();
    }

    /// <summary>锁行幂等 ensure-insert + SQLite 集群警告(§3.4/§10.4)。</summary>
    protected virtual async Task EnsureInitializedAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (_initialized) return;
        if (!await db.Queryable<SysJobLock>().AnyAsync(l => l.Id == SysJobLock.SingletonId))
        {
            try
            {
                await db.Insertable(new SysJobLock
                {
                    Id = SysJobLock.SingletonId,
                    OwnerNodeName = "",
                    LeaseUntil = now.AddSeconds(-1),
                    Term = 0,
                }).ExecuteCommandAsync();
            }
            catch (Exception ex)   // 并发副本同刻首插:撞主键即吞,再确认行在
            {
                if (!await db.Queryable<SysJobLock>().AnyAsync(l => l.Id == SysJobLock.SingletonId)) throw;
                logger.LogDebug(ex, "sys_job_lock 已被并发副本创建,忽略。");
            }
        }
        if (string.Equals(dbOptions.DbType, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var aliveSince = now.AddSeconds(-options.LeaseSeconds * 2);
            var others = await db.Queryable<SysJobNode>()
                .Where(n => n.NodeName != NodeName && n.LastHeartbeat > aliveSince)
                .CountAsync();
            if (others > 0)
                logger.LogWarning("SQLite 下检测到第二个活跃调度节点——SQLite 不支持集群形态(锁争用/损坏风险),集群/Worker 请改用服务器数据库(§10.4)。");
        }
        _initialized = true;
    }

    /// <summary>
    /// 回收孤儿执行记录:节点被 <c>kill -9</c>/断电后,它插下的 Running 行永远闭合不了,
    /// 而未闭合行正是 SerialSkip 的调度输入——不收,该任务从此每次都被判"上次还在跑"而永久停摆
    /// (kill 端点写旗标没人轮询、清空与狗粮都刻意保留未闭合行,唯一出路是人工 SQL)。
    /// <para>判活依据:<b>节点名 + 进程实例 Id</b> 同时对应一条新鲜心跳。仅比 NodeName 不够——
    /// 同名进程重启会先刷新心跳,旧实例遗留的 Running 行会被误判存活。仅主节点执行,每拍一次。</para>
    /// </summary>
    protected virtual async Task<int> ReapOrphanRunsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var deadline = now.AddSeconds(-options.LeaseSeconds * 2);
        var alivePairs = await db.Queryable<SysJobNode>()
            .Where(n => n.LastHeartbeat > deadline)
            .Select(n => new { n.NodeName, n.InstanceId })
            .ToListAsync();
        var aliveKeys = new HashSet<string>(
            alivePairs.Select(n => AliveKey(n.NodeName, n.InstanceId)),
            StringComparer.Ordinal);

        // 先筛未闭合且足够陈旧的候选,再在内存里按实例键判活(四库通吃,避免元组 Contains 方言差)
        var candidates = await db.Queryable<SysJobLog>()
            .Where(l => l.EndTime == null && l.StartTime < deadline)
            .Select(l => new { l.Id, l.NodeName, l.NodeInstanceId })
            .ToListAsync();
        var orphans = candidates
            .Where(l => !aliveKeys.Contains(AliveKey(l.NodeName, l.NodeInstanceId)))
            .Select(l => l.Id)
            .ToList();
        if (orphans.Count == 0) return 0;

        var reaped = await db.Updateable<SysJobLog>()
            .SetColumns(l => new SysJobLog
            {
                EndTime = now,
                RunStatus = JobRunStatus.Cancelled,
                ErrorText = "执行节点失联或进程实例已更替,按崩溃/重启推定终止——本行由主节点回收闭合,实际结果未知。",
            })
            .Where(l => orphans.Contains(l.Id) && l.EndTime == null)
            .ExecuteCommandAsync();
        if (reaped > 0)
            logger.LogWarning("TenonAdmin 调度器:回收 {Count} 条失联/更替实例遗留的未闭合执行记录。", reaped);
        return reaped;
    }

    private static string AliveKey(string nodeName, string? instanceId) => $"{nodeName}\0{instanceId ?? ""}";

    /// <summary>心跳:节点行 upsert + 主节点续租 / 备节点夺租(§5.2)。全部参数化 UPDATE 按影响行数判定,四库通吃。</summary>
    protected virtual async Task HeartbeatAsync(DateTime now, CancellationToken cancellationToken)
    {
        await UpsertNodeAsync(now, cancellationToken);
        var leaseUntil = now.AddSeconds(options.LeaseSeconds);
        if (_isLeader)
        {
            var renewed = await db.Updateable<SysJobLock>()
                .SetColumns(l => new SysJobLock { LeaseUntil = leaseUntil })
                .Where(l => l.Id == SysJobLock.SingletonId && l.OwnerNodeName == NodeName)
                .ExecuteCommandAsync();
            if (renewed != 1)
            {
                _isLeader = false;   // 立刻自认失主;在飞任务不杀——它们已被 CAS 领走,天然安全
                logger.LogWarning("TenonAdmin 调度器:续租失败,节点 {Node} 退回备节点。", NodeName);
            }
            return;
        }
        // 夺取:租约过期,或锁本就属于本节点名(进程重启后即刻收回,不等租约烂完)
        var acquired = await db.Updateable<SysJobLock>()
            .SetColumns(l => new SysJobLock { OwnerNodeName = NodeName, LeaseUntil = leaseUntil, Term = l.Term + 1 })
            .Where(l => l.Id == SysJobLock.SingletonId && (l.LeaseUntil < now || l.OwnerNodeName == NodeName))
            .ExecuteCommandAsync();
        if (acquired == 1)
        {
            _isLeader = true;
            _dirty = true;   // 新主全量重载
            logger.LogInformation("TenonAdmin 调度器:节点 {Node} 当选主节点。", NodeName);
        }
    }

    /// <summary>节点行 upsert(每拍;所有节点含备节点都写,监控页据此展示集群)。同名重启时一并覆写 InstanceId/StartTime。</summary>
    protected virtual async Task UpsertNodeAsync(DateTime now, CancellationToken cancellationToken)
    {
        _nodeStartTime ??= now;
        // SqlSugar SetColumns 表达式会求值闭包成员;idOptions.WorkerId 为 null 时 ?? 会炸(issue #24)。
        // 先提局部变量,表达式里只出现常量/局部,不碰可空成员链。
        var instanceId = executor.InstanceId;
        var workerId = idOptions.WorkerId ?? 0;
        var pid = Environment.ProcessId;
        var hostName = Environment.MachineName;
        var startTime = _nodeStartTime.Value;
        var updated = await db.Updateable<SysJobNode>()
            .SetColumns(n => new SysJobNode
            {
                LastHeartbeat = now,
                Pid = pid,
                HostName = hostName,
                WorkerId = workerId,
                InstanceId = instanceId,
                StartTime = startTime,
            })
            .Where(n => n.NodeName == NodeName)
            .ExecuteCommandAsync();
        if (updated != 0) return;
        try
        {
            await db.Insertable(new SysJobNode
            {
                Id = idGenerator.NextId(),
                NodeName = NodeName,
                InstanceId = instanceId,
                HostName = hostName,
                Pid = pid,
                WorkerId = workerId,
                StartTime = startTime,
                LastHeartbeat = now,
            }).ExecuteCommandAsync();
        }
        catch (Exception)   // 并发首插撞唯一索引 → 回退为更新(含 InstanceId,避免旧实例键滞留)
        {
            await db.Updateable<SysJobNode>()
                .SetColumns(n => new SysJobNode
                {
                    LastHeartbeat = now,
                    Pid = pid,
                    HostName = hostName,
                    WorkerId = workerId,
                    InstanceId = instanceId,
                    StartTime = startTime,
                })
                .Where(n => n.NodeName == NodeName)
                .ExecuteCommandAsync();
        }
    }

    /// <summary>
    /// 全量重载 Ready 行(任务量级几十行,整表重载零成本,不做版本戳)。
    /// 顺带修 NextRunTime 为空的 Ready 行:补算(种子行/enable 复活走这里)或判死置 Completed。
    /// </summary>
    protected virtual async Task ReloadJobsAsync(DateTime now, CancellationToken cancellationToken)
    {
        _dirty = false;
        _lastReload = now;
        var rows = await db.Queryable<SysJob>().Where(j => j.Status == JobStatus.Ready).ToListAsync();
        var cache = new List<SysJob>(rows.Count);
        foreach (var job in rows)
        {
            if (job.NextRunTime is null)
            {
                var next = JobTrigger.ComputeNext(job, now);
                if (next is { } value)
                {
                    var backfilled = await db.Updateable<SysJob>()
                        .SetColumns(j => new SysJob { NextRunTime = value })
                        .Where(j => j.Id == job.Id && j.Status == JobStatus.Ready && j.NextRunTime == null)
                        .ExecuteCommandAsync();
                    if (backfilled != 1)
                    {
                        // 并发副本抢先补算了:单行回读拿它算出的时刻,别干等下一轮重载
                        // (ReloadSeconds 若被配得大于 MisfireThresholdSeconds,干等会把正常触发降级成 misfire)
                        var fresh = await db.Queryable<SysJob>()
                            .Where(j => j.Id == job.Id && j.Status == JobStatus.Ready && j.NextRunTime != null)
                            .FirstAsync();
                        if (fresh is null) continue;
                        cache.Add(fresh);
                        continue;
                    }
                    job.NextRunTime = value;
                }
                else
                {
                    // 判死也要带 NextRunTime IS NULL:并发编辑可能刚把新时刻写进去(整表快照是旧影像),
                    // 少了这半句会把用户刚救活的任务判死,而且留下 Completed + 非空 NextRunTime 违反 §2.2 不变量
                    await db.Updateable<SysJob>()
                        .SetColumns(j => new SysJob { Status = JobStatus.Completed })
                        .Where(j => j.Id == job.Id && j.Status == JobStatus.Ready && j.NextRunTime == null)
                        .ExecuteCommandAsync();
                    logger.LogWarning("任务 {Code} 已无未来触发时刻,置 Completed。", job.Code);
                    continue;
                }
            }
            cache.Add(job);
        }
        _cache = cache;
    }

    /// <summary>派发到期任务:限流 → 错过判定 → 领取 CAS → 触发/记账(§5.3)。</summary>
    protected virtual async Task DispatchDueJobsAsync(DateTime now, CancellationToken cancellationToken)
    {
        foreach (var job in _cache.Where(j => j.NextRunTime is { } due && due <= now).OrderBy(j => j.NextRunTime).ToList())
        {
            if (executor.InFlightCount >= options.MaxConcurrentRuns)
            {
                logger.LogWarning("在飞执行数已达上限({Max}),本拍不再领取,下拍再来(47013 语义)。", options.MaxConcurrentRuns);
                break;
            }
            var expected = job.NextRunTime!.Value;
            var isMisfire = (now - expected).TotalSeconds > options.MisfireThresholdSeconds;
            var next = JobTrigger.ComputeNext(job, now);
            if (!await ClaimAsync(job, expected, next, now))
            {
                await RefreshSingleAsync(job);   // 被别的节点领走 / 行被改:单行回读
                continue;
            }
            job.NextRunTime = next;
            if (next is null) _cache.Remove(job);   // 无未来时刻:OneShot 由执行器收尾,其余由下轮重载判死

            if (isMisfire && job.MisfireStrategy == JobMisfireStrategy.Skip)
            {
                await InsertMissedSkippedLogAsync(job, expected, now);
                continue;
            }
            var fireMode = isMisfire ? JobFireMode.Misfire : JobFireMode.Schedule;
            // 跨节点:库里未闭合行(含别的副本)仍须查;本机 SerialSkip/容量由 TryFire 原子占位,避免 check-then-act
            if (job.ConcurrencyMode == JobConcurrencyMode.SerialSkip
                && await db.Queryable<SysJobLog>().AnyAsync(l => l.JobId == job.Id && l.EndTime == null))
            {
                await InsertSerialSkippedLogAsync(job, expected, now);
                continue;
            }
            var fireResult = executor.TryFireAndTrack(job, expected, fireMode, out _);
            if (fireResult == JobFireResult.AlreadyRunning)
            {
                await InsertSerialSkippedLogAsync(job, expected, now);
                continue;
            }
            if (fireResult is JobFireResult.LimitReached or JobFireResult.Draining)
            {
                logger.LogWarning("在飞执行数已达上限或宿主排水中,本拍停止领取(47013 语义)。");
                break;
            }
        }
    }

    /// <summary>
    /// 领取 CAS——防双发的<b>唯一</b>正确性来源(§5.2):影响行数 = 1 才允许触发。
    /// <c>NextRunTime = @expected</c> 那半句是命根子,删掉它双发测试必须红(§12 变异判据)。
    /// </summary>
    protected virtual async Task<bool> ClaimAsync(SysJob job, DateTime expected, DateTime? next, DateTime now)
    {
        var affected = await db.Updateable<SysJob>()
            .SetColumns(j => new SysJob { NextRunTime = next, LastRunTime = now, NumberOfRuns = j.NumberOfRuns + 1 })
            .Where(j => j.Id == job.Id && j.NextRunTime == expected && j.Status == JobStatus.Ready && !j.IsDelete)
            .ExecuteCommandAsync();
        return affected == 1;
    }

    /// <summary>领取失败后的单行回读(被别人领走/被编辑/被删)。</summary>
    protected virtual async Task RefreshSingleAsync(SysJob job)
    {
        var fresh = await db.Queryable<SysJob>().Where(j => j.Id == job.Id && j.Status == JobStatus.Ready).FirstAsync();
        var index = _cache.IndexOf(job);
        if (fresh is null)
        {
            if (index >= 0) _cache.RemoveAt(index);
        }
        else if (index >= 0)
        {
            _cache[index] = fresh;
        }
    }

    /// <summary>Skip 策略:错过合并记一行 MissedSkipped(记次数,不刷表,§2.3)。</summary>
    protected virtual Task InsertMissedSkippedLogAsync(SysJob job, DateTime expected, DateTime now)
    {
        var missed = JobTrigger.CountMissed(job, expected, now);
        return db.Insertable(new SysJobLog
        {
            JobId = job.Id,
            JobName = job.Name,
            FireInstanceId = idGenerator.NextId(),
            FireMode = JobFireMode.MissedSkipped,
            ScheduledTime = expected,
            StartTime = now,
            EndTime = now,
            RunStatus = JobRunStatus.Skipped,
            NodeName = NodeName,
            NodeInstanceId = executor.InstanceId,
            MessageText = $"错过 {(missed >= 1000 ? "≥1000" : missed.ToString())} 次触发(最早 {expected:yyyy-MM-dd HH:mm:ss}),按 Skip 策略不补跑,直接推进到未来时刻。",
        }).ExecuteCommandAsync();
    }

    /// <summary>SerialSkip:上次触发未结束,本次跳过并记 Skipped 行(丢弃可见,§2.3)。</summary>
    protected virtual Task InsertSerialSkippedLogAsync(SysJob job, DateTime expected, DateTime now)
        => db.Insertable(new SysJobLog
        {
            JobId = job.Id,
            JobName = job.Name,
            FireInstanceId = idGenerator.NextId(),
            FireMode = JobFireMode.Schedule,
            ScheduledTime = expected,
            StartTime = now,
            EndTime = now,
            RunStatus = JobRunStatus.Skipped,
            NodeName = NodeName,
            NodeInstanceId = executor.InstanceId,
            MessageText = "上次触发尚未结束(存在未闭合执行记录),SerialSkip 跳过本次。",
        }).ExecuteCommandAsync();

    /// <summary>deadline 睡眠:睡到 min(最近到期时刻, 心跳节拍);任务变更/停机即刻唤醒。</summary>
    protected virtual async Task SleepUntilNextBeatAsync(DateTime? nextDue, CancellationToken stoppingToken)
    {
        var now = time.GetLocalNow().DateTime;
        var delay = TimeSpan.FromSeconds(options.HeartbeatSeconds);
        if (nextDue is { } due && due - now < delay) delay = due - now;
        if (delay <= TimeSpan.Zero) delay = TimeSpan.FromMilliseconds(50);   // 已到期:让一口气,防热旋

        CancellationTokenSource wake;
        lock (_wakeLock)
        {
            wake = _wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }
        try
        {
            // 复查 _dirty:变更事件若落在上一拍返回后、本次 wake 安装前,WakeUp 对着 null 空转,
            // 少了这一句就会睡满一整拍(「本进程改任务即刻生效」的承诺在此窗口失效)
            if (_dirty) return;
            await Task.Delay(delay, time, wake.Token);
        }
        catch (OperationCanceledException)
        {
            // 被变更事件或停机唤醒,都正常
        }
        finally
        {
            lock (_wakeLock)
            {
                if (ReferenceEquals(_wake, wake)) _wake = null;
                wake.Dispose();
            }
        }
    }

    private void WakeUp()
    {
        lock (_wakeLock)
        {
            try { _wake?.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }
}
