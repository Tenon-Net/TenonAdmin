using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 内置任务种子:执行记录清理(狗粮任务,docs/scheduling-ledger.md §7.3)。
/// Id=1 —— sys_job 自有 Id 空间,内核段 [1,1000),SeedIdRangeTests 自动看护。
/// </summary>
internal sealed class DefaultJobSeed : ISeedData<SysJob>
{
    /// <summary>内置清理任务的编码(排障/文档的稳定锚点)</summary>
    internal const string LOG_CLEANUP_CODE = "sys-job-log-cleanup";

    /// <summary>
    /// <b>刻意 false(与菜单种子相反),理由留档:</b>job 行是运行态可变数据——NextRunTime/计数器/
    /// 用户改过的 cron 全在同一行,升级刷回种子值 = 清空运行态 + 吞掉用户调参。
    /// 菜单是内核拥有的纯结构件才敢开 SyncOnUpgrade。
    /// </summary>
    public bool SyncOnUpgrade => false;

    public IEnumerable<SysJob> HasData() =>
    [
        new SysJob
        {
            Id = 1,
            Code = LOG_CLEANUP_CODE,
            Name = "执行记录清理",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(JobLogCleanupJob).FullName!,   // IAdminJob.Name 默认 = 类型全名

            TriggerKind = JobTriggerKind.Cron,
            CronExpression = "0 30 3 * * ?",   // 每天 03:30,已是归一化 6 段
            MisfireStrategy = JobMisfireStrategy.Skip,
            ConcurrencyMode = JobConcurrencyMode.SerialSkip,
            Status = JobStatus.Ready,
            // NextRunTime 留空:调度器重载时对 Ready 且 NextRunTime 为空的行按触发配置补算(§5.3),
            // 种子在编写期没有时钟,不硬编码时刻。
            IsSystem = true,
            AlertByNotice = true,
            Remark = "删除过期执行记录(保留天数见配置 sys.job.logRetentionDays),并顺手清理失联超 24h 的调度节点行",
        },
    ];
}
