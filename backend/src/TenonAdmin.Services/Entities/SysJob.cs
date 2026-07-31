using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 定时任务表——一行完整声明一个任务:触发配置 + 载荷 + 失败策略 + 运行状态(docs/scheduling-ledger.md §2/§3.2)。
/// 一任务 = 一触发(ADR-0004 决策三):同一段业务逻辑要两套时刻表 = 建两行。
/// <para>基类用 <see cref="BaseEntity"/>(软删 + 回收站)而<b>不是</b> DataEntity:任务是全局运维对象,
/// 调度循环在后台线程跑、无 HTTP 上下文,挂 IOrgScoped 会被数据范围过滤器搅局。</para>
/// <para><see cref="NextRunTime"/> 是<b>领取列</b>:每次触发前对它做原子 CAS(§5.2),防双发的唯一正确性来源;
/// 所有写入路径必须先整秒截断(MySQL datetime 毫秒四舍五入会让 CAS 无声失效,§13-9)。</para>
/// </summary>
[SugarTable("sys_job", TableDescription = "定时任务")]
[SugarIndex("uk_sys_job_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_sys_job_next", nameof(NextRunTime), OrderByType.Asc)]
public class SysJob : BaseEntity
{
    /// <summary>任务编码(唯一;种子/日志/排障的稳定锚点,冲突 → 47002)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "任务编码(唯一)")]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128, ColumnDescription = "任务名称")]
    public string Name { get; set; } = "";

    [SugarColumn(ColumnDescription = "载荷类型:1=编译类/2=HTTP/3=SQL")]
    public JobHandlerKind HandlerKind { get; set; } = JobHandlerKind.Compiled;

    /// <summary>编译类 = 处理器标识(IAdminJob.Name,默认类型全名);HTTP/SQL 由服务端固定填内置处理器名</summary>
    [SugarColumn(Length = 256, ColumnDescription = "处理器标识")]
    public string HandlerName { get; set; } = "";

    /// <summary>属性包:Dictionary&lt;string,string?&gt; JSON,处理器参数的唯一入口(键表见台账 §7)</summary>
    /// <remarks>SqlServer 禁止裸 text(非 Unicode);见 <c>SysJobLog.ErrorText</c> 同注。</remarks>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "属性包(JSON 字符串字典)")]
    public string? PropsJson { get; set; }

    [SugarColumn(ColumnDescription = "触发类型:1=Cron/2=固定间隔/3=一次性")]
    public JobTriggerKind TriggerKind { get; set; } = JobTriggerKind.Cron;

    /// <summary>6 段秒级 cron(入库前归一化 + 统一大写,§4.2)</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "cron 表达式(归一化 6 段)")]
    public string? CronExpression { get; set; }

    /// <summary>固定间隔秒数,≥5(&lt;5 拒 47004,防日志表爆炸,§13-7)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "固定间隔(秒,≥5)")]
    public int? IntervalSeconds { get; set; }

    /// <summary>一次性执行时刻(已过去 → 47004)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "一次性执行时刻")]
    public DateTime? OneShotTime { get; set; }

    /// <summary>生效窗口起点(空 = 立即生效)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "生效窗口起点")]
    public DateTime? StartTime { get; set; }

    /// <summary>生效窗口终点(过点置 Completed)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "生效窗口终点")]
    public DateTime? EndTime { get; set; }

    [SugarColumn(ColumnDescription = "错过策略:1=Skip 不补/2=FireOnceNow 补一次")]
    public JobMisfireStrategy MisfireStrategy { get; set; } = JobMisfireStrategy.Skip;

    [SugarColumn(ColumnDescription = "并发模式:1=串行跳过/2=并行")]
    public JobConcurrencyMode ConcurrencyMode { get; set; } = JobConcurrencyMode.SerialSkip;

    [SugarColumn(ColumnDescription = "状态:1=Ready/2=Paused/3=Completed/4=Panic")]
    public JobStatus Status { get; set; } = JobStatus.Ready;

    /// <summary>下次执行时刻 = 领取列(整秒;Paused/Completed/Panic 时为 NULL)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "下次执行时刻(领取列,整秒)")]
    public DateTime? NextRunTime { get; set; }

    /// <summary>最近一次领取时刻</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最近执行时刻")]
    public DateTime? LastRunTime { get; set; }

    [SugarColumn(ColumnDescription = "累计触发次数")]
    public long NumberOfRuns { get; set; }

    [SugarColumn(ColumnDescription = "累计失败次数")]
    public long NumberOfErrors { get; set; }

    /// <summary>连续失败计数(成功清零;达 FailAlertThreshold → Panic + 告警)</summary>
    [SugarColumn(ColumnDescription = "连续失败次数")]
    public int ConsecutiveErrors { get; set; }

    /// <summary>单次执行超时秒数,0=不限;超时取消本次执行并记 Timeout</summary>
    [SugarColumn(ColumnDescription = "执行超时(秒,0=不限)")]
    public int TimeoutSeconds { get; set; }

    /// <summary>单次触发内的重试次数(0=不重试)</summary>
    [SugarColumn(ColumnDescription = "失败重试次数")]
    public int RetryCount { get; set; }

    [SugarColumn(ColumnDescription = "重试间隔(秒)")]
    public int RetryIntervalSeconds { get; set; }

    /// <summary>连败达此值 → 发告警 + 转 Panic;0=不告警不 Panic</summary>
    [SugarColumn(ColumnDescription = "连败告警阈值(0=关)")]
    public int FailAlertThreshold { get; set; }

    /// <summary>告警走站内信(Notice 定向发任务创建人 + 超管,不广播)</summary>
    [SugarColumn(ColumnDescription = "告警走站内信")]
    public bool AlertByNotice { get; set; } = true;

    /// <summary>告警邮件收件人(逗号分隔);空 → 回退 sys_config 的 sys.job.alertEmails</summary>
    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "告警邮件收件人")]
    public string? AlertEmails { get; set; }

    /// <summary>内核种子任务 = true:禁删(47014),可暂停、可改触发配置</summary>
    [SugarColumn(ColumnDescription = "内置任务(禁删)")]
    public bool IsSystem { get; set; }

    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
