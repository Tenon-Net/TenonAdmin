namespace TenonAdmin.Services;

/// <summary>
/// 定时任务的 sys_config 键(两层配置约定:运行期可调的旋钮进配置中心、改值即时生效;
/// 结构性参数——心跳/租约/围栏——留在 <c>TenonAdmin:Jobs</c> 节,改它值得重启一次)。
/// </summary>
public static class JobConfigKeys
{
    /// <summary>配置分组</summary>
    public const string GROUP = "job";

    /// <summary>执行记录保留天数(JobLogCleanupJob 按此清 sys_job_log;≤0 不清理)</summary>
    public const string KEY_LOG_RETENTION_DAYS = "sys.job.logRetentionDays";

    /// <summary>连败告警邮件的全局兜底收件人(逗号分隔;任务行 AlertEmails 非空时优先任务行)</summary>
    public const string KEY_ALERT_EMAILS = "sys.job.alertEmails";
}
