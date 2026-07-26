using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>任务新增/编辑入参。<c>Code</c> 仅新增时生效——更新时服务层忽略,创建后不可变(它是排障锚点)。</summary>
public record JobInput
{
    /// <summary>任务编码(唯一,创建后不可变)</summary>
    public string Code { get; init; } = "";

    /// <summary>任务名称</summary>
    public string Name { get; init; } = "";

    /// <summary>载荷类型</summary>
    public JobHandlerKind HandlerKind { get; init; } = JobHandlerKind.Compiled;

    /// <summary>处理器标识(编译类填 IAdminJob.Name;HTTP/SQL 由服务端固定填内置处理器名,此处传什么都会被覆盖)</summary>
    public string HandlerName { get; init; } = "";

    /// <summary>属性包(处理器参数;HTTP 的 url/headers、SQL 的 sql、编译类的自定义参数)</summary>
    public IReadOnlyDictionary<string, string?>? Properties { get; init; }

    /// <summary>触发类型</summary>
    public JobTriggerKind TriggerKind { get; init; } = JobTriggerKind.Cron;

    /// <summary>cron 表达式(TriggerKind=Cron 必填;入库前归一化为 6 段大写)</summary>
    public string? CronExpression { get; init; }

    /// <summary>固定间隔秒数(TriggerKind=Interval 必填,≥5)</summary>
    public int? IntervalSeconds { get; init; }

    /// <summary>一次性执行时刻(TriggerKind=OneShot 必填,须为将来)</summary>
    public DateTime? OneShotTime { get; init; }

    /// <summary>生效窗口起点(可选)</summary>
    public DateTime? StartTime { get; init; }

    /// <summary>生效窗口终点(可选)</summary>
    public DateTime? EndTime { get; init; }

    /// <summary>错过策略</summary>
    public JobMisfireStrategy MisfireStrategy { get; init; } = JobMisfireStrategy.Skip;

    /// <summary>并发模式</summary>
    public JobConcurrencyMode ConcurrencyMode { get; init; } = JobConcurrencyMode.SerialSkip;

    /// <summary>执行超时(秒,0=不限)</summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>失败重试次数</summary>
    public int RetryCount { get; init; }

    /// <summary>重试间隔(秒)</summary>
    public int RetryIntervalSeconds { get; init; }

    /// <summary>连败告警阈值(0=不告警不 Panic)</summary>
    public int FailAlertThreshold { get; init; }

    /// <summary>告警走站内信</summary>
    public bool AlertByNotice { get; init; } = true;

    /// <summary>告警邮件收件人(逗号分隔;空则回退全局配置)</summary>
    public string? AlertEmails { get; init; }

    /// <summary>备注</summary>
    public string? Remark { get; init; }
}

/// <summary>任务分页查询入参</summary>
public record JobPageInput : PageInputBase
{
    /// <summary>任务名称(模糊,可选)</summary>
    public string? Name { get; init; }

    /// <summary>状态(精确,可选)</summary>
    public JobStatus? Status { get; init; }

    /// <summary>载荷类型(精确,可选)</summary>
    public JobHandlerKind? HandlerKind { get; init; }
}

/// <summary>执行记录分页查询入参</summary>
public record JobLogPageInput : PageInputBase
{
    /// <summary>任务 Id(精确,可选)</summary>
    public long? JobId { get; init; }

    /// <summary>执行结果(精确,可选)</summary>
    public JobRunStatus? RunStatus { get; init; }

    /// <summary>开始时刻下界(可选)</summary>
    public DateTime? StartFrom { get; init; }

    /// <summary>开始时刻上界(可选)</summary>
    public DateTime? StartTo { get; init; }
}

/// <summary>清空执行记录入参</summary>
public record JobLogClearInput
{
    /// <summary>只删这么多天以前的记录;为空 = 删全部(未闭合的运行中记录一律保留)</summary>
    public int? BeforeDays { get; init; }

    /// <summary>只删该任务的记录;为空 = 全部任务</summary>
    public long? JobId { get; init; }
}

/// <summary>cron 预览入参(POST:cron 含 <c>? #</c>,走 query 有转义坑)</summary>
public record CronPreviewInput
{
    /// <summary>cron 表达式(5 或 6 段)</summary>
    public string Cron { get; init; } = "";

    /// <summary>预览条数(默认 5,上限 20)</summary>
    public int Count { get; init; } = 5;

    /// <summary>起算时刻(默认当前服务器时间)</summary>
    public DateTime? From { get; init; }
}

/// <summary>cron 预览结果</summary>
public record CronPreviewOutput
{
    /// <summary>归一化后的 6 段表达式(入库形态)</summary>
    public string Normalized { get; init; } = "";

    /// <summary>未来若干次触发时刻(可能少于请求条数,甚至为空 = 已无未来时刻)</summary>
    public IReadOnlyList<DateTime> Occurrences { get; init; } = [];

    /// <summary>秒段等效每秒执行的告警(前端提示用;不硬拦,故意写的算深思熟虑)</summary>
    public bool EverySecondWarning { get; init; }
}

/// <summary>监控仪表盘输出(前端 15s 轮询)</summary>
public record JobDashboardOutput
{
    /// <summary>今日成功次数</summary>
    public int TodaySuccess { get; init; }

    /// <summary>今日失败次数(含超时)</summary>
    public int TodayFailed { get; init; }

    /// <summary>当前运行中(未闭合执行记录数)</summary>
    public int Running { get; init; }

    /// <summary>任务总数(未删)</summary>
    public int TotalJobs { get; init; }

    /// <summary>按状态的任务数</summary>
    public IReadOnlyDictionary<string, int> StatusCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>近 14 日成败趋势(按日,含零值日)</summary>
    public IReadOnlyList<JobTrendPoint> Trend { get; init; } = [];

    /// <summary>即将执行的前 10 次</summary>
    public IReadOnlyList<JobUpcomingItem> Upcoming { get; init; } = [];

    /// <summary>集群节点表(角色由与锁行比对得出,不落库)</summary>
    public IReadOnlyList<JobNodeItem> Nodes { get; init; } = [];
}

/// <summary>成败趋势的一天</summary>
public record JobTrendPoint(string Date, int Success, int Failed);

/// <summary>即将执行的一项</summary>
public record JobUpcomingItem(long JobId, string Name, DateTime NextRunTime);

/// <summary>集群节点一行</summary>
public record JobNodeItem(string NodeName, string HostName, bool IsLeader, DateTime LastHeartbeat, int WorkerId, int Pid);
