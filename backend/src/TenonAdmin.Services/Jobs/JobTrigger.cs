using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 触发配置的纯函数运算:下一次时刻与错过次数(scheduling-ledger §2.3/§5.3)。
/// 调度循环与 JobService(新增/更新/启用时算首个 NextRunTime)共用,无状态。
/// </summary>
public static class JobTrigger
{
    /// <summary>
    /// 严格大于 <paramref name="after"/> 的下一次触发时刻(整秒);无未来时刻返回 null(调用侧置 Completed)。
    /// 生效窗口:未到 StartTime 时从 StartTime 起算(含 StartTime 本身);越过 EndTime 即无解。
    /// </summary>
    public static DateTime? ComputeNext(SysJob job, DateTime after)
    {
        var from = job.StartTime is { } start && start > after ? start.AddSeconds(-1) : after;
        DateTime? next = job.TriggerKind switch
        {
            JobTriggerKind.Cron when !string.IsNullOrWhiteSpace(job.CronExpression)
                && CronExpression.TryParse(job.CronExpression!, out var cron) => cron!.GetNextOccurrence(from),
            // 间隔任务锚在"上一次推进点"(调用方传 now):节奏 = 执行完推进,不追历史(xxl-job 同款)
            JobTriggerKind.Interval when job.IntervalSeconds is > 0 => JobTime.Truncate(from).AddSeconds(job.IntervalSeconds.Value),
            JobTriggerKind.OneShot when job.OneShotTime is { } at && at > after => at,
            _ => null,
        };
        if (next is not { } n) return null;
        n = JobTime.Truncate(n);
        return job.EndTime is { } end && n > end ? null : n;
    }

    /// <summary>错过次数:(expected, now] 内本应触发的次数,1000 封顶(MissedSkipped 记账用,不刷表)。</summary>
    public static int CountMissed(SysJob job, DateTime expected, DateTime now)
    {
        switch (job.TriggerKind)
        {
            case JobTriggerKind.Interval when job.IntervalSeconds is > 0:
                var byInterval = (long)(now - expected).TotalSeconds / job.IntervalSeconds.Value + 1;
                return (int)Math.Min(byInterval, 1000);
            case JobTriggerKind.Cron when !string.IsNullOrWhiteSpace(job.CronExpression)
                && CronExpression.TryParse(job.CronExpression!, out var cron):
                var count = 1;
                var cursor = expected;
                while (count < 1000 && cron!.GetNextOccurrence(cursor) is { } n && n <= now)
                {
                    count++;
                    cursor = n;
                }
                return count;
            default:
                return 1;
        }
    }
}
