using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 任务执行器(scheduling-ledger §5.4):一次触发(FireInstance)的完整生命周期——
/// 开 scope 解析处理器 → 插 Running 记录行 → 执行(超时/终止/停机三路取消)→ 闭合行 → 重试 →
/// 计数与 Panic 告警。步骤全 <c>protected virtual</c>,消费者按模板方法覆写单步。
/// <para><b>可订阅类型</b>(dev-plan §2.5):后续新增构造参数一律给默认值,不破源码兼容。</para>
/// <para>取消语义:超时(TimeoutSeconds)→ Timeout;手动 kill / 停机 drain 逾期 → Cancelled;
/// <b>取消不重试</b>,只有 Failed 走重试循环。Cancelled 不计失败,Failed/Timeout 计入连败。</para>
/// </summary>
public class JobExecutor(
    IServiceScopeFactory scopeFactory,
    IJobHandlerResolver resolver,
    ISqlSugarClient db,
    AdminJobsOptions options,
    AdminIdOptions idOptions,
    IIdGenerator idGenerator,
    TimeProvider time,
    ILogger<JobExecutor> logger)
{
    private readonly ConcurrentDictionary<long, RunRegistration> _running = new();   // logId → 在跑登记(kill 快路径)
    private readonly ConcurrentDictionary<long, Task> _fires = new();                // fireInstanceId → 整次触发(drain 用)
    private readonly ConcurrentDictionary<long, int> _busyJobs = new();              // jobId → 本机在飞次数(SerialSkip 的同机即时视图)
    private readonly CancellationTokenSource _drainCts = new();                       // drain 硬停:唤醒重试等待
    private readonly object _fireGate = new();                                       // SerialSkip 占位 + 全局容量 + 开 fire 的原子区
    private volatile bool _draining;

    private sealed record RunRegistration(long JobId, CancellationTokenSource KillCts);

    /// <summary>
    /// 本机是否正在跑该任务。SerialSkip 的库查询是 check-then-act:领取到插 Running 行之间有窗口
    /// (线程池饥饿时可达秒级),期间库里查不到未闭合行 → 串行任务被并行双跑。本表在 <see cref="TryFireAndTrack"/> 里
    /// <b>同步</b>登记,把同节点的窗口彻底闭死;跨节点残余窗口(主切换瞬间)仍在,见台账 §13。
    /// </summary>
    public virtual bool IsBusyLocally(long jobId) => _busyJobs.ContainsKey(jobId);

    /// <summary>本节点名(写进执行记录 NodeName 列)</summary>
    public string NodeName { get; } = JobTime.ResolveNodeName(options, idOptions);

    /// <summary>
    /// 本进程实例 Id(启动时生成,不可复用)。写进节点行与执行记录,孤儿回收按「节点名 + 实例」判活,
    /// 避免同名重启后误把旧进程遗留的 Running 行当成仍在飞。
    /// </summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>在飞触发数(MaxConcurrentRuns 的比较对象;一次触发含其全部重试算 1)</summary>
    public int InFlightCount => _fires.Count;

    /// <summary>
    /// 发起一次触发并登记(fire-and-forget:返回的 Task 永不抛,调度循环可弃;run-now 端点可等)。
    /// 内部走 <see cref="TryFireAndTrack"/>:容量满 / SerialSkip 占位失败时返回已完成 Task。
    /// </summary>
    public virtual Task FireAndTrack(SysJob job, DateTime scheduledTime, JobFireMode fireMode)
        => TryFireAndTrack(job, scheduledTime, fireMode, out var task) == JobFireResult.Started
            ? task!
            : Task.CompletedTask;

    /// <summary>
    /// 原子地完成「SerialSkip 本地占位 + 全局容量预留 + 创建 fire task」。
    /// 服务层不应在调用前自行做可被并发绕过的分离检查;跨节点 SerialSkip 仍需查库未闭合行。
    /// </summary>
    public virtual JobFireResult TryFireAndTrack(SysJob job, DateTime scheduledTime, JobFireMode fireMode, out Task? fireTask)
    {
        fireTask = null;
        long fireInstanceId;
        Task task;
        lock (_fireGate)
        {
            if (_draining) return JobFireResult.Draining;
            if (job.ConcurrencyMode == JobConcurrencyMode.SerialSkip && _busyJobs.ContainsKey(job.Id))
                return JobFireResult.AlreadyRunning;
            if (_fires.Count >= options.MaxConcurrentRuns)
                return JobFireResult.LimitReached;

            fireInstanceId = idGenerator.NextId();
            _busyJobs.AddOrUpdate(job.Id, 1, (_, n) => n + 1);   // 同步登记:SerialSkip 检查立刻看得见
            // SuppressFlow 是必须的:SqlSugarScope 按 AsyncLocal 上下文隔离连接,而 ExecutionContext 会流进
            // Task.Run——不掐断,fire 任务与调度循环共用同一连接,并发查询直接炸 reader。
            // 占位释放必须在 fire task 自己的 finally 里做完,不能 ContinueWith:await FireAndTrack
            // 返回后 ContinueWith 可能还没跑,紧接着的第二次 TryFire 会误判 AlreadyRunning 空跑
            // (Panic 连败阈值等「await 完再点一次」路径会 flake)。
            using (ExecutionContext.SuppressFlow())
            {
                var jobId = job.Id;
                task = Task.Run(async () =>
                {
                    try
                    {
                        await RunFireAsync(job, scheduledTime, fireMode, fireInstanceId);
                    }
                    finally
                    {
                        lock (_fireGate)
                        {
                            _fires.TryRemove(fireInstanceId, out Task? _);
                            _busyJobs.AddOrUpdate(jobId, 0, (_, n) => n - 1);
                            _busyJobs.TryRemove(new KeyValuePair<long, int>(jobId, 0));
                        }
                    }
                });
            }
            _fires[fireInstanceId] = task;
            // check-then-add 复查:登记与 drain 置位非原子,极窄窗口内溜进来的 fire 在此被追认并取消
            if (_draining) _drainCts.Cancel();
        }
        fireTask = task;
        return JobFireResult.Started;
    }

    /// <summary>终止本机在跑的一次执行(kill 端点的快路径;执行在别的节点时走 KillRequested 旗标轮询)。</summary>
    public virtual bool TryCancelLocal(long logId)
    {
        if (!_running.TryGetValue(logId, out var registration)) return false;
        try { registration.KillCts.Cancel(); }
        catch (ObjectDisposedException) { return false; }   // 恰好刚结束
        return true;
    }

    /// <summary>
    /// 停机排水(§5.3):停发新触发 → 宽限期内等在飞收尾 → 逾期(hardStop 触发)逐个 Cancel 再等闭合。
    /// </summary>
    public virtual async Task DrainAsync(CancellationToken hardStop)
    {
        _draining = true;
        var pending = _fires.Values.ToArray();
        if (pending.Length == 0) return;
        try
        {
            await Task.WhenAll(pending).WaitAsync(hardStop);
        }
        catch (OperationCanceledException)
        {
            // 逾期硬停:①取消在跑的处理器;②唤醒还在重试等待里的 fire(它们不在 _running 里,
            // 单靠遍历 _running 取消不到,会在停机后开跑全新一次尝试并把 StopAsync 卡住)。
            _drainCts.Cancel();
            foreach (var registration in _running.Values)
            {
                try { registration.KillCts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            try { await Task.WhenAll(_fires.Values.ToArray()); }
            catch { /* RunFireAsync 不外抛,保险 */ }
        }
    }

    /// <summary>一次触发:重试循环 + 完成后的计数/状态/告警。绝不外抛(fire-and-forget 的底线)。</summary>
    protected virtual async Task RunFireAsync(SysJob job, DateTime scheduledTime, JobFireMode fireMode, long fireInstanceId)
    {
        try
        {
            var outcome = JobRunStatus.Failed;
            string? lastError = null;
            var retryCount = Math.Max(0, job.RetryCount);
            for (var retryIndex = 0; retryIndex <= retryCount; retryIndex++)
            {
                bool retryable;
                (outcome, lastError, retryable) = await RunAttemptAsync(job, scheduledTime, fireMode, fireInstanceId, retryIndex);
                if (outcome != JobRunStatus.Failed || !retryable) break;   // 成功收工;超时/取消/处理器缺失不重试(§5.4)
                if (retryIndex >= retryCount) break;
                if (job.RetryIntervalSeconds > 0)
                {
                    // 挂 drain 令牌:硬停时重试等待必须能被打断,否则停机后还会开跑全新一次尝试、把 StopAsync 卡死
                    try { await Task.Delay(TimeSpan.FromSeconds(job.RetryIntervalSeconds), time, _drainCts.Token); }
                    catch (OperationCanceledException) { break; }
                }
                if (_drainCts.IsCancellationRequested) break;   // 硬停后不再开新尝试
            }
            await OnFireCompletedAsync(job, fireMode, outcome, lastError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "任务 {Code} 的触发处理出现内部异常(已吞,调度不受影响)。", job.Code);
        }
    }

    /// <summary>单次尝试:插行 → 解析 → 执行 → 闭合;返回(结果,末次错误,是否可重试)。</summary>
    protected virtual async Task<(JobRunStatus Outcome, string? Error, bool Retryable)> RunAttemptAsync(
        SysJob job, DateTime scheduledTime, JobFireMode fireMode, long fireInstanceId, int retryIndex)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var startedAt = JobTime.Truncate(time.GetLocalNow().DateTime);
        var log = new SysJobLog
        {
            JobId = job.Id,
            JobName = job.Name,
            FireInstanceId = fireInstanceId,
            RetryIndex = retryIndex,
            FireMode = fireMode,
            ScheduledTime = JobTime.Truncate(scheduledTime),
            StartTime = startedAt,
            RunStatus = JobRunStatus.Running,
            NodeName = NodeName,
            NodeInstanceId = InstanceId,
        };
        await db.Insertable(log).ExecuteCommandAsync();   // AOP 填雪花 Id/CreateTime

        var handler = await resolver.ResolveAsync(job.HandlerName, scope.ServiceProvider);
        if (handler is null)
        {
            var missing = $"处理器未注册:{job.HandlerName}(47005 语义;编译类处理器需 TryAddEnumerable 注册,GET /handlers 可查清单)";
            await CloseLogAsync(log.Id, JobRunStatus.Failed, 0, null, missing);
            return (JobRunStatus.Failed, missing, false);   // 重试也不会凭空长出处理器
        }

        using var killCts = new CancellationTokenSource();
        using var timeoutCts = job.TimeoutSeconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(job.TimeoutSeconds)) : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(killCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(killCts.Token, timeoutCts.Token);
        _running[log.Id] = new RunRegistration(job.Id, killCts);
        using var pollStop = new CancellationTokenSource();
        // 同 FireAndTrack 的 SuppressFlow 理由:轮询与处理器执行并发,必须各占一个 SqlSugarScope 上下文
        Task pollTask;
        using (ExecutionContext.SuppressFlow())
        {
            pollTask = Task.Run(() => PollKillFlagAsync(log.Id, killCts, pollStop.Token));
        }

        var stopwatch = Stopwatch.StartNew();
        var messages = new StringBuilder();
        var context = new JobExecutionContext
        {
            JobId = job.Id,
            JobCode = job.Code,
            JobName = job.Name,
            FireInstanceId = fireInstanceId,
            RetryIndex = retryIndex,
            FireMode = fireMode,
            ScheduledTime = log.ScheduledTime,
            FireTime = startedAt,
            Properties = ParseProps(job.PropsJson, messages),
            Log = text => AppendCapped(messages, text),
        };

        try
        {
            await handler.ExecuteAsync(context, linked.Token);
            // 处理器可能压根没观察令牌(SqlSugar 的 Ado 执行就是这样):await 正常返回不等于没超时。
            // 返回后复查取消状态,否则一条跑过头的 SQL 会被记成 Success,超时与 kill 对它双双失效。
            if (linked.IsCancellationRequested)
            {
                var (lateStatus, lateNote) = Interpret(job, timeoutCts, ignoredToken: true);
                await CloseLogAsync(log.Id, lateStatus, stopwatch.ElapsedMilliseconds, Render(messages), lateNote);
                return (lateStatus, lateNote, false);
            }
            await CloseLogAsync(log.Id, JobRunStatus.Success, stopwatch.ElapsedMilliseconds, Render(messages), null);
            return (JobRunStatus.Success, null, false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            var (status, note) = Interpret(job, timeoutCts, ignoredToken: false);
            await CloseLogAsync(log.Id, status, stopwatch.ElapsedMilliseconds, Render(messages), note);
            return (status, note, false);
        }
        catch (Exception ex)
        {
            var error = Cap(ex.ToString(), 8192);
            await CloseLogAsync(log.Id, JobRunStatus.Failed, stopwatch.ElapsedMilliseconds, Render(messages), error);
            return (JobRunStatus.Failed, ex.Message, true);
        }
        finally
        {
            _running.TryRemove(log.Id, out _);
            pollStop.Cancel();
            try { await pollTask; }
            catch { /* 轮询收尾异常不外传 */ }
        }
    }

    /// <summary>取消原因判读:超时 vs 手动终止/停机;<paramref name="ignoredToken"/> 标记处理器没理会令牌(跑完才发现)。</summary>
    private static (JobRunStatus Status, string Note) Interpret(SysJob job, CancellationTokenSource? timeoutCts, bool ignoredToken)
    {
        var timedOut = timeoutCts?.IsCancellationRequested == true;
        var note = timedOut ? $"执行超时({job.TimeoutSeconds}s)" : "已被终止(手动 kill 或宿主停机)";
        if (ignoredToken) note += ";处理器未响应取消令牌,实际跑到自然结束(见台账 §13:任务实现必须传递取消令牌)";
        return (timedOut ? JobRunStatus.Timeout : JobRunStatus.Cancelled, note);
    }

    /// <summary>跨节点终止:每 KillPollSeconds 按主键读<b>自己那行</b>的 KillRequested 旗标(§5.4)。</summary>
    protected virtual async Task PollKillFlagAsync(long logId, CancellationTokenSource killCts, CancellationToken stopPolling)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.KillPollSeconds));
        while (!stopPolling.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, time, stopPolling);
                var killed = await db.Queryable<SysJobLog>().Where(l => l.Id == logId).Select(l => l.KillRequested).FirstAsync();
                if (killed)
                {
                    try { killCts.Cancel(); }
                    catch (ObjectDisposedException) { }
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "kill 旗标轮询一拍失败(忽略,下拍再来)。");
            }
        }
    }

    /// <summary>闭合执行记录行。</summary>
    protected virtual Task CloseLogAsync(long logId, JobRunStatus status, long elapsedMs, string? message, string? error)
    {
        var endedAt = JobTime.Truncate(time.GetLocalNow().DateTime);
        return db.Updateable<SysJobLog>()
            .SetColumns(l => new SysJobLog { EndTime = endedAt, RunStatus = status, ElapsedMs = elapsedMs, MessageText = message, ErrorText = error })
            .Where(l => l.Id == logId)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 一次触发收尾:计数、OneShot 完结、连败 Panic 与告警(§5.4)。
    /// 告警只发"跨阈那一次"——Ready→Panic 的状态 CAS 恰好只成功一次,天然去重且 Panic 后不再调度不刷屏。
    /// </summary>
    protected virtual async Task OnFireCompletedAsync(SysJob job, JobFireMode fireMode, JobRunStatus outcome, string? lastError)
    {
        if (fireMode == JobFireMode.Manual)
            await db.Updateable<SysJob>()
                .SetColumns(j => new SysJob { NumberOfRuns = j.NumberOfRuns + 1 })
                .Where(j => j.Id == job.Id)
                .ExecuteCommandAsync();   // 调度触发的计数在领取 CAS 里,手动触发在这补

        if (outcome == JobRunStatus.Success)
        {
            await db.Updateable<SysJob>()
                .SetColumns(j => new SysJob { ConsecutiveErrors = 0 })
                .Where(j => j.Id == job.Id)
                .ExecuteCommandAsync();
            if (job.TriggerKind == JobTriggerKind.OneShot && fireMode != JobFireMode.Manual)
                await db.Updateable<SysJob>()
                    .SetColumns(j => new SysJob { Status = JobStatus.Completed, NextRunTime = null })
                    .Where(j => j.Id == job.Id && j.Status == JobStatus.Ready)
                    .ExecuteCommandAsync();
            return;
        }
        if (outcome == JobRunStatus.Cancelled) return;   // 手动终止/停机不计失败

        await db.Updateable<SysJob>()
            .SetColumns(j => new SysJob { NumberOfErrors = j.NumberOfErrors + 1, ConsecutiveErrors = j.ConsecutiveErrors + 1 })
            .Where(j => j.Id == job.Id)
            .ExecuteCommandAsync();
        if (job.FailAlertThreshold <= 0) return;

        var consecutive = await db.Queryable<SysJob>().Where(j => j.Id == job.Id).Select(j => j.ConsecutiveErrors).FirstAsync();
        if (consecutive < job.FailAlertThreshold) return;

        var becamePanic = await db.Updateable<SysJob>()
            .SetColumns(j => new SysJob { Status = JobStatus.Panic, NextRunTime = null })
            .Where(j => j.Id == job.Id && j.Status == JobStatus.Ready)
            .ExecuteCommandAsync();
        if (becamePanic != 1) return;   // 已 Panic/已被人改状态:不重复告警

        logger.LogWarning("任务 {Code} 连续失败 {Count} 次,已转入 Panic 停摆(告警仅此一次)。", job.Code, consecutive);
        await SendPanicAlertAsync(job, consecutive, lastError);
    }

    /// <summary>发 Panic 告警:站内信定向(创建人 + 超管,不广播)+ 邮件(任务行收件人,空则回退全局配置)。失败只记日志。</summary>
    protected virtual async Task SendPanicAlertAsync(SysJob job, int consecutive, string? lastError)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var title = $"定时任务连续失败:{job.Name}";
        var body = $"任务「{job.Name}」({job.Code})连续失败 {consecutive} 次,已转入 Panic 停摆,需人工在任务页重新启用。"
                   + (string.IsNullOrEmpty(lastError) ? "" : $"\n最后错误:{Cap(lastError!, 500)}");

        // 站内信与邮件彼此独立:接收目标校验失败(QA25)或精简宿主缺用户表时,不能拖死邮件通道。
        if (job.AlertByNotice)
        {
            try
            {
                var receivers = new HashSet<long> { SuperAdminSeed.SUPER_ADMIN_ID };
                if (job.CreateUserId is > 0) receivers.Add(job.CreateUserId.Value);
                await scope.ServiceProvider.GetRequiredService<INoticeService>().PublishAsync(new NoticePublishInput
                {
                    Title = title,
                    Content = body,
                    ReceiverType = ReceiverType.User,
                    ReceiverIds = receivers.ToArray(),
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "任务 {Code} 的 Panic 站内信告警发送失败。", job.Code);
            }
        }

        try
        {
            var recipients = SplitEmails(job.AlertEmails);
            if (recipients.Length == 0)
                recipients = SplitEmails(await scope.ServiceProvider.GetRequiredService<IConfigService>()
                    .GetValueByKeyAsync(JobConfigKeys.KEY_ALERT_EMAILS));
            if (recipients.Length == 0) return;

            var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            foreach (var address in recipients)
            {
                try { await email.SendAsync(address, title, body); }
                catch (Exception ex) { logger.LogWarning(ex, "任务告警邮件发送失败:{Address}。", address); }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "任务 {Code} 的 Panic 邮件告警发送失败(不影响调度)。", job.Code);
        }
    }

    // ── 小工具 ───────────────────────────────────────────────────────

    private static string[] SplitEmails(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? [] : raw!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyDictionary<string, string?> ParseProps(string? propsJson, StringBuilder messages)
    {
        if (string.IsNullOrWhiteSpace(propsJson)) return new Dictionary<string, string?>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(propsJson!) ?? new Dictionary<string, string?>();
        }
        catch (JsonException)
        {
            AppendCapped(messages, "属性包 PropsJson 不是合法 JSON 字符串字典,按空属性包执行(47011 语义)。");
            return new Dictionary<string, string?>();
        }
    }

    /// <summary>追加一条处理器输出;只受 8KB 总量上限约束(单条不再另设小上限——那会让 Http.MaxResponseLogBytes 调大后失效)。</summary>
    private static void AppendCapped(StringBuilder messages, string text)
    {
        lock (messages)
        {
            var room = 8192 - messages.Length;
            if (room <= 0) return;
            messages.AppendLine(Cap(text, room));
        }
    }

    private static string? Render(StringBuilder messages)
    {
        lock (messages)
        {
            return messages.Length == 0 ? null : Cap(messages.ToString(), 8192);
        }
    }

    private static string Cap(string text, int max) => text.Length <= max ? text : text[..max];
}
