using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IJobService"/> 默认实现(scheduling-ledger §8)。每步 <c>virtual</c>,消费者可单步覆写。
/// </summary>
public class JobService(
    IRepository<SysJob> jobs,
    ISqlSugarClient db,
    JobExecutor executor,
    IEnumerable<IAdminJob> handlers,
    AdminJobsOptions options,
    IEventBus events,
    TimeProvider time) : IJobService
{
    /// <summary>
    /// 属性包里的密钥占位符。HTTP 任务的 headers 常含 <c>Authorization</c>,而任务<b>读</b>权限严格弱于任务编辑权——
    /// 列表接口整行下发就等于把凭据发给所有能看任务的人。故读出时把 headers 的值换成本占位符;
    /// 保存时值仍是占位符即视为"不改",从库里取回原值(前端因此无需特殊处理)。
    /// </summary>
    public const string SecretMask = "********";

    /// <inheritdoc />
    public virtual async Task<PagedList<SysJob>> PageAsync(JobPageInput input)
    {
        var page = await jobs.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Name), j => j.Name.Contains(input.Name!))
            .WhereIF(input.Status is not null, j => j.Status == input.Status!.Value)
            .WhereIF(input.HandlerKind is not null, j => j.HandlerKind == input.HandlerKind!.Value)
            .OrderBy(j => j.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);
        foreach (var row in page.Items) row.PropsJson = MaskSecrets(row.PropsJson);
        return page;
    }

    /// <summary>把属性包里 headers 的各值换成占位符(其余键原样;非法 JSON 原样返回)。</summary>
    protected virtual string? MaskSecrets(string? propsJson)
    {
        if (string.IsNullOrWhiteSpace(propsJson)) return propsJson;
        Dictionary<string, string?>? props;
        try { props = JsonSerializer.Deserialize<Dictionary<string, string?>>(propsJson!); }
        catch (JsonException) { return propsJson; }
        if (props is null || !props.TryGetValue("headers", out var headers) || string.IsNullOrWhiteSpace(headers)) return propsJson;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(headers!);
            if (parsed is null) return propsJson;
            props["headers"] = JsonSerializer.Serialize(parsed.ToDictionary(kv => kv.Key, _ => SecretMask));
        }
        catch (JsonException) { props["headers"] = SecretMask; }
        return JsonSerializer.Serialize(props);
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(JobInput input)
    {
        // 查重纳入软删行:唯一索引覆盖已软删行,漏检会撞库唯一约束抛原生 500(Dict 同款教训)
        AdminException.ThrowIf(
            await jobs.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(j => j.Code == input.Code),
            ErrorCode.JobCodeExists);

        var entity = new SysJob { Code = input.Code };
        ApplyInput(entity, input, null);
        entity.Status = JobStatus.Ready;
        entity.NextRunTime = JobTrigger.ComputeNext(entity, Now);
        if (entity.NextRunTime is null) entity.Status = JobStatus.Completed;
        await jobs.InsertAsync(entity);
        await events.PublishAsync(new JobChangedEvent(entity.Id));
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, JobInput input)
    {
        var entity = await GetAsync(id);
        var originalNext = entity.NextRunTime;
        ApplyInput(entity, input, entity.PropsJson);   // Code 不动:创建后不可变

        // 定向更新,不碰运行态列(NextRunTime/LastRunTime/计数器/Status)。
        // 整行盲写会把读取瞬间之后由领取 CAS 推进的 NextRunTime 写回旧值——同一 occurrence 会被领第二次(双发),
        // 顺带覆盖掉计数器与连败数、搅乱 Panic 判阈。
        await db.Updateable<SysJob>()
            .SetColumns(j => new SysJob
            {
                Name = entity.Name,
                HandlerKind = entity.HandlerKind,
                HandlerName = entity.HandlerName,
                PropsJson = entity.PropsJson,
                TriggerKind = entity.TriggerKind,
                CronExpression = entity.CronExpression,
                IntervalSeconds = entity.IntervalSeconds,
                OneShotTime = entity.OneShotTime,
                StartTime = entity.StartTime,
                EndTime = entity.EndTime,
                MisfireStrategy = entity.MisfireStrategy,
                ConcurrencyMode = entity.ConcurrencyMode,
                TimeoutSeconds = entity.TimeoutSeconds,
                RetryCount = entity.RetryCount,
                RetryIntervalSeconds = entity.RetryIntervalSeconds,
                FailAlertThreshold = entity.FailAlertThreshold,
                AlertByNotice = entity.AlertByNotice,
                AlertEmails = entity.AlertEmails,
                Remark = entity.Remark,
                UpdateTime = Now,
            })
            .Where(j => j.Id == id)
            .ExecuteCommandAsync();

        // 触发配置变了才重算下次时刻,且带 CAS:若这期间调度器已经领取推进过,让它的值胜出
        // (新配置在下一次推进时自然生效,不必回头改这一格)。
        if (entity.Status == JobStatus.Ready)
        {
            var next = JobTrigger.ComputeNext(entity, Now);
            await db.Updateable<SysJob>()
                .SetColumns(j => new SysJob { NextRunTime = next })
                .Where(j => j.Id == id && j.Status == JobStatus.Ready
                    && (originalNext == null ? j.NextRunTime == null : j.NextRunTime == originalNext))
                .ExecuteCommandAsync();
            if (next is null)
                await db.Updateable<SysJob>()
                    .SetColumns(j => new SysJob { Status = JobStatus.Completed })
                    .Where(j => j.Id == id && j.Status == JobStatus.Ready && j.NextRunTime == null)
                    .ExecuteCommandAsync();
        }
        await events.PublishAsync(new JobChangedEvent(id));
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        var entity = await GetAsync(id);
        AdminException.ThrowIf(entity.IsSystem, ErrorCode.JobProtected);
        await jobs.DeleteAsync(id);
        await events.PublishAsync(new JobChangedEvent(id));
    }

    /// <inheritdoc />
    public virtual async Task DeleteBatchAsync(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return;
        AdminException.ThrowIf(
            await jobs.AsQueryable().AnyAsync(j => ids.Contains(j.Id) && j.IsSystem),
            ErrorCode.JobProtected);
        foreach (var id in ids) await jobs.DeleteAsync(id);
        await events.PublishAsync(new JobChangedEvent(0));   // 0 = 批量变更,订阅侧只用它当"脏"信号
    }

    /// <inheritdoc />
    public virtual async Task SetEnabledAsync(long id, bool enabled)
    {
        var entity = await GetAsync(id);
        if (enabled)
        {
            entity.NextRunTime = JobTrigger.ComputeNext(entity, Now);
            // 一次性任务跑完/过了生效窗口 → 没有未来时刻可恢复,响亮拒绝而不是留个假 Ready
            AdminException.ThrowIf(entity.NextRunTime is null, ErrorCode.JobStatusConflict);
            entity.Status = JobStatus.Ready;
            entity.ConsecutiveErrors = 0;
        }
        else
        {
            entity.Status = JobStatus.Paused;
            entity.NextRunTime = null;   // 异常态不留下次时刻(§2.2)
        }
        await jobs.UpdateAsync(entity);
        await events.PublishAsync(new JobChangedEvent(id));
    }

    /// <inheritdoc />
    public virtual async Task RunOnceAsync(long id)
    {
        var entity = await GetAsync(id);
        if (entity.ConcurrencyMode == JobConcurrencyMode.SerialSkip)
            AdminException.ThrowIf(
                await db.Queryable<SysJobLog>().AnyAsync(l => l.JobId == id && l.EndTime == null),
                ErrorCode.JobAlreadyRunning);
        AdminException.ThrowIf(executor.InFlightCount >= options.MaxConcurrentRuns, ErrorCode.JobRunLimitReached);
        // 本机执行、不动 NextRunTime:调度节奏不受手动触发干扰(§8 端点 7)
        _ = executor.FireAndTrack(entity, Now, JobFireMode.Manual);
    }

    /// <inheritdoc />
    public virtual CronPreviewOutput PreviewCron(CronPreviewInput input)
    {
        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(input.Cron);
        }
        catch (FormatException ex)
        {
            throw new AdminException(ErrorCode.JobCronInvalid, new Dictionary<string, object?> { ["reason"] = ex.Message }, ex.Message);
        }
        var count = Math.Clamp(input.Count, 1, 20);
        var from = input.From ?? Now;
        return new CronPreviewOutput
        {
            Normalized = cron.Expression,
            Occurrences = cron.GetNextOccurrences(from, count),
            // 秒段为 * = 每秒一发:不硬拦(故意写的算深思熟虑),但前端要给告警(§13-7)
            EverySecondWarning = cron.Expression.Split(' ')[0] == "*",
        };
    }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> ListHandlers() =>
        handlers.Select(h => h.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <inheritdoc />
    public virtual async Task<JobDashboardOutput> GetDashboardAsync()
    {
        var now = Now;
        var today = now.Date;
        var since = today.AddDays(-13);

        var recent = await db.Queryable<SysJobLog>()
            .Where(l => l.StartTime >= since)
            .Select(l => new { l.StartTime, l.RunStatus, l.EndTime })
            .ToListAsync();
        var allJobs = await jobs.AsQueryable().Select(j => new { j.Id, j.Name, j.Status, j.NextRunTime }).ToListAsync();
        var nodes = await db.Queryable<SysJobNode>().OrderBy(n => n.NodeName).ToListAsync();
        var leader = await db.Queryable<SysJobLock>().Where(l => l.Id == SysJobLock.SingletonId).FirstAsync();

        static bool IsFailure(JobRunStatus status) => status is JobRunStatus.Failed or JobRunStatus.Timeout;
        var trend = Enumerable.Range(0, 14)
            .Select(offset => since.AddDays(offset))
            .Select(day => new JobTrendPoint(
                day.ToString("yyyy-MM-dd"),
                recent.Count(l => l.StartTime.Date == day && l.RunStatus == JobRunStatus.Success),
                recent.Count(l => l.StartTime.Date == day && IsFailure(l.RunStatus))))
            .ToList();

        return new JobDashboardOutput
        {
            TodaySuccess = recent.Count(l => l.StartTime.Date == today && l.RunStatus == JobRunStatus.Success),
            TodayFailed = recent.Count(l => l.StartTime.Date == today && IsFailure(l.RunStatus)),
            Running = await db.Queryable<SysJobLog>().Where(l => l.EndTime == null).CountAsync(),
            TotalJobs = allJobs.Count,
            StatusCounts = Enum.GetValues<JobStatus>().ToDictionary(s => s.ToString(), s => allJobs.Count(j => j.Status == s)),
            Trend = trend,
            Upcoming = allJobs.Where(j => j.NextRunTime is not null && j.Status == JobStatus.Ready)
                .OrderBy(j => j.NextRunTime)
                .Take(10)
                .Select(j => new JobUpcomingItem(j.Id, j.Name, j.NextRunTime!.Value))
                .ToList(),
            Nodes = nodes.Select(n => new JobNodeItem(
                    n.NodeName, n.HostName,
                    leader is not null && leader.OwnerNodeName == n.NodeName && leader.LeaseUntil >= now,
                    n.LastHeartbeat, n.WorkerId, n.Pid))
                .ToList(),
        };
    }

    /// <summary>取任务,不存在抛 <see cref="ErrorCode.JobNotFound"/>。</summary>
    protected virtual async Task<SysJob> GetAsync(long id)
    {
        var entity = await jobs.GetByIdAsync(id);
        AdminException.ThrowIf(entity is null, ErrorCode.JobNotFound);
        return entity!;
    }

    /// <summary>入参落到实体:载荷归一 + 触发配置校验 + 属性包序列化(<paramref name="storedProps"/> 供占位符回填)。</summary>
    protected virtual void ApplyInput(SysJob entity, JobInput input, string? storedProps)
    {
        entity.Name = input.Name;
        entity.HandlerKind = input.HandlerKind;
        entity.HandlerName = ResolveHandlerName(input);
        entity.PropsJson = ValidateAndSerializeProps(input, storedProps);
        entity.TriggerKind = input.TriggerKind;
        entity.MisfireStrategy = input.MisfireStrategy;
        entity.ConcurrencyMode = input.ConcurrencyMode;
        entity.TimeoutSeconds = Math.Max(0, input.TimeoutSeconds);
        entity.RetryCount = Math.Max(0, input.RetryCount);
        entity.RetryIntervalSeconds = Math.Max(0, input.RetryIntervalSeconds);
        entity.FailAlertThreshold = Math.Max(0, input.FailAlertThreshold);
        entity.AlertByNotice = input.AlertByNotice;
        entity.AlertEmails = input.AlertEmails;
        entity.Remark = input.Remark;
        entity.StartTime = input.StartTime is { } s ? JobTime.Truncate(s) : null;
        entity.EndTime = input.EndTime is { } e ? JobTime.Truncate(e) : null;
        ApplyTrigger(entity, input);
    }

    /// <summary>触发配置校验与归一(cron 47003 / 其余 47004)。</summary>
    protected virtual void ApplyTrigger(SysJob entity, JobInput input)
    {
        entity.CronExpression = null;
        entity.IntervalSeconds = null;
        entity.OneShotTime = null;
        switch (input.TriggerKind)
        {
            case JobTriggerKind.Cron:
                AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.CronExpression), ErrorCode.JobTriggerInvalid);
                try
                {
                    entity.CronExpression = CronExpression.Normalize(input.CronExpression!);
                }
                catch (FormatException ex)
                {
                    throw new AdminException(ErrorCode.JobCronInvalid, new Dictionary<string, object?> { ["reason"] = ex.Message }, ex.Message);
                }
                break;
            case JobTriggerKind.Interval:
                // 下限 5 秒:2s 任务 ≈ 4.3 万行/天,日志表会爆(§13-7)
                AdminException.ThrowIf(input.IntervalSeconds is not >= 5, ErrorCode.JobTriggerInvalid);
                entity.IntervalSeconds = input.IntervalSeconds;
                break;
            case JobTriggerKind.OneShot:
                AdminException.ThrowIf(input.OneShotTime is not { } at || at <= Now, ErrorCode.JobTriggerInvalid);
                entity.OneShotTime = JobTime.Truncate(input.OneShotTime!.Value);
                break;
            default:
                throw new AdminException(ErrorCode.JobTriggerInvalid);
        }
        AdminException.ThrowIf(entity.StartTime is { } start && entity.EndTime is { } end && end <= start, ErrorCode.JobTriggerInvalid);
    }

    /// <summary>HTTP/SQL 的处理器名由服务端固定(消费者填什么都不算数);编译类必须在已注册清单里。</summary>
    protected virtual string ResolveHandlerName(JobInput input) => input.HandlerKind switch
    {
        JobHandlerKind.Http => typeof(HttpAdminJob).FullName!,
        JobHandlerKind.Sql => typeof(SqlAdminJob).FullName!,
        _ => ValidateCompiledHandler(input.HandlerName),
    };

    private string ValidateCompiledHandler(string handlerName)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(handlerName), ErrorCode.JobHandlerNotFound);
        AdminException.ThrowIf(
            !ListHandlers().Contains(handlerName, StringComparer.Ordinal),
            ErrorCode.JobHandlerNotFound,
            new Dictionary<string, object?> { ["handlerName"] = handlerName });
        // 内置的 HTTP/SQL 处理器也在 IAdminJob 清单里,选 Compiled + 填它们的类型全名就能整体跳过
        // 入库侧的围栏与 SQL 总闸(执行侧还拦得住,但纵深从两层掉到一层,且 47009/47008 保存时不报)。
        // 内置载荷只能经对应的 HandlerKind 选。
        AdminException.ThrowIf(
            handlerName == typeof(HttpAdminJob).FullName || handlerName == typeof(SqlAdminJob).FullName,
            ErrorCode.JobPropsInvalid,
            new Dictionary<string, object?> { ["key"] = "handlerName" });
        return handlerName;
    }

    /// <summary>
    /// 属性包校验与序列化:HTTP 查 url 并过围栏(47009)+ 逐条校验请求头(拦 CRLF 走私),
    /// SQL 查 sql 并过总闸(47008)。<paramref name="storedProps"/> 非空时,值为掩码的 header 从库里取回原值。
    /// </summary>
    protected virtual string? ValidateAndSerializeProps(JobInput input, string? storedProps)
    {
        var props = new Dictionary<string, string?>(input.Properties ?? new Dictionary<string, string?>());
        switch (input.HandlerKind)
        {
            case JobHandlerKind.Http:
                var url = Prop(props, "url");
                AdminException.ThrowIf(string.IsNullOrWhiteSpace(url), ErrorCode.JobPropsInvalid, new Dictionary<string, object?> { ["key"] = "url" });
                JobHttpFence.ValidateUrl(url!, options.Http);
                var headers = Prop(props, "headers");
                if (!string.IsNullOrWhiteSpace(headers))
                {
                    Dictionary<string, string?>? parsed;
                    try { parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(headers!); }
                    catch (JsonException)
                    {
                        throw new AdminException(ErrorCode.JobPropsInvalid, new Dictionary<string, object?> { ["key"] = "headers" });
                    }
                    if (parsed is not null)
                    {
                        var previous = ReadStoredHeaders(storedProps);
                        foreach (var key in parsed.Keys.ToList())
                        {
                            // 值仍是掩码 = 前端没改这条,取回库里的原值(掩码本身不能被当成密钥存回去)
                            if (parsed[key] == SecretMask && previous.TryGetValue(key, out var original))
                                parsed[key] = original;
                            JobHttpFence.ValidateHeader(key, parsed[key]);
                        }
                        props["headers"] = JsonSerializer.Serialize(parsed);
                    }
                }
                break;
            case JobHandlerKind.Sql:
                AdminException.ThrowIf(!options.Sql.Enabled, ErrorCode.JobSqlDisabled);
                AdminException.ThrowIf(string.IsNullOrWhiteSpace(Prop(props, "sql")), ErrorCode.JobPropsInvalid, new Dictionary<string, object?> { ["key"] = "sql" });
                break;
        }
        return props.Count == 0 ? null : JsonSerializer.Serialize(props);
    }

    private static Dictionary<string, string?> ReadStoredHeaders(string? storedProps)
    {
        if (string.IsNullOrWhiteSpace(storedProps)) return [];
        try
        {
            var props = JsonSerializer.Deserialize<Dictionary<string, string?>>(storedProps!);
            if (props is null || !props.TryGetValue("headers", out var headers) || string.IsNullOrWhiteSpace(headers)) return [];
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(headers!) ?? [];
        }
        catch (JsonException) { return []; }
    }

    private static string? Prop(IReadOnlyDictionary<string, string?> props, string key) =>
        props.TryGetValue(key, out var v) ? v : null;

    private DateTime Now => JobTime.Truncate(time.GetLocalNow().DateTime);
}

/// <summary><see cref="IJobLogService"/> 默认实现。</summary>
public class JobLogService(ISqlSugarClient db, JobExecutor executor, TimeProvider time) : IJobLogService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<SysJobLog>> PageAsync(JobLogPageInput input) =>
        await db.Queryable<SysJobLog>()
            .WhereIF(input.JobId is not null, l => l.JobId == input.JobId!.Value)
            .WhereIF(input.RunStatus is not null, l => l.RunStatus == input.RunStatus!.Value)
            .WhereIF(input.StartFrom is not null, l => l.StartTime >= input.StartFrom!.Value)
            .WhereIF(input.StartTo is not null, l => l.StartTime <= input.StartTo!.Value)
            .WhereIF(input.FireInstanceId is not null, l => l.FireInstanceId == input.FireInstanceId!.Value)
            .OrderBy(l => l.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    public virtual async Task KillAsync(long logId)
    {
        var row = await db.Queryable<SysJobLog>().Where(l => l.Id == logId).FirstAsync();
        AdminException.ThrowIf(row is null, ErrorCode.JobLogNotFound);
        AdminException.ThrowIf(row!.EndTime is not null, ErrorCode.JobRunNotAlive);
        await db.Updateable<SysJobLog>()
            .SetColumns(l => new SysJobLog { KillRequested = true })
            .Where(l => l.Id == logId)
            .ExecuteCommandAsync();
        executor.TryCancelLocal(logId);   // 同节点即刻;异节点靠对方 KillPollSeconds 轮询这行旗标
    }

    /// <inheritdoc />
    public virtual async Task<int> ClearAsync(JobLogClearInput input)
    {
        var cutoff = input.BeforeDays is { } days && days > 0
            ? JobTime.Truncate(time.GetLocalNow().DateTime).AddDays(-days)
            : (DateTime?)null;
        return await db.Deleteable<SysJobLog>()
            .Where(l => l.EndTime != null)   // 运行中的记录一律保留:删了就再也不知道它在跑
            .WhereIF(cutoff is not null, l => l.CreateTime < cutoff!.Value)
            .WhereIF(input.JobId is not null, l => l.JobId == input.JobId!.Value)
            .ExecuteCommandAsync();
    }
}
