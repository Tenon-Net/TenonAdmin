namespace TenonAdmin.Services;

/// <summary>任务载荷类型(<c>sys_job.HandlerKind</c>,docs/scheduling-ledger.md §7)</summary>
public enum JobHandlerKind
{
    /// <summary>编译类:IAdminJob 实现,HandlerName 按 Name 匹配(GET /handlers 下拉可选)</summary>
    Compiled = 1,

    /// <summary>HTTP 请求:属性包给 url/method/headers/body,SSRF 围栏见 §7.1</summary>
    Http = 2,

    /// <summary>SQL 语句:属性包给 sql;总闸 Jobs:Sql:Enabled 默认关(§7.2)</summary>
    Sql = 3,
}

/// <summary>触发类型(<c>sys_job.TriggerKind</c>)</summary>
public enum JobTriggerKind
{
    /// <summary>6 段秒级 cron(入库前归一化,§4)</summary>
    Cron = 1,

    /// <summary>固定间隔(IntervalSeconds ≥ 5)</summary>
    Interval = 2,

    /// <summary>一次性(OneShotTime;成功后置 Completed)</summary>
    OneShot = 3,
}

/// <summary>错过(misfire)处理策略:到期时刻迟到超过 MisfireThresholdSeconds 才触发本策略</summary>
public enum JobMisfireStrategy
{
    /// <summary>不补跑,直接推进到首个未来时刻;错过合并记一行 MissedSkipped(默认)</summary>
    Skip = 1,

    /// <summary>立即补跑一次(FireMode=Misfire)再推进;错过再多也只补一次,不回放</summary>
    FireOnceNow = 2,
}

/// <summary>并发模式:上一次触发未结束时,本次怎么办(刻意不做「排队」,§16)</summary>
public enum JobConcurrencyMode
{
    /// <summary>跳过并记一行 Skipped(默认;存在未闭合 log 行即视为在跑)</summary>
    SerialSkip = 1,

    /// <summary>不检查,放行并跑</summary>
    Parallel = 2,
}

/// <summary>
/// 任务状态(<c>sys_job.Status</c>,§2.2)。<b>刻意没有 Running 态</b>——「正在运行」由未闭合的
/// 执行记录行(<c>sys_job_log.EndTime IS NULL</c>)推导,进程崩溃不会留下卡死的 Running 任务。
/// 进入 Paused/Completed/Panic 时 NextRunTime 置 NULL,扫表条件天然排除。
/// </summary>
public enum JobStatus
{
    /// <summary>参与调度(NextRunTime 非空)</summary>
    Ready = 1,

    /// <summary>人工暂停,不调度;回收站恢复的任务也强制进这里</summary>
    Paused = 2,

    /// <summary>一次性任务已执行 / 已过 EndTime;终态但可 enable 复活(重算出未来时刻才回 Ready)</summary>
    Completed = 3,

    /// <summary>连续失败达 FailAlertThreshold:停止调度、已发告警,等人工 enable 恢复</summary>
    Panic = 4,
}

/// <summary>单次执行结果(<c>sys_job_log.RunStatus</c>)</summary>
public enum JobRunStatus
{
    /// <summary>运行中(EndTime 为空)</summary>
    Running = 1,

    /// <summary>成功</summary>
    Success = 2,

    /// <summary>失败(处理器抛异常 / HTTP 状态不符)</summary>
    Failed = 3,

    /// <summary>超时被取消(TimeoutSeconds)</summary>
    Timeout = 4,

    /// <summary>被取消(手动终止 / 宿主停机)</summary>
    Cancelled = 5,

    /// <summary>SerialSkip 跳过(上次触发未结束)</summary>
    Skipped = 6,
}
