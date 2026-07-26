using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>定时任务模块的时间与节点名纪律(scheduling-ledger §4.3/§13-9)。</summary>
internal static class JobTime
{
    /// <summary>
    /// 整秒截断——<b>所有</b>写库的调度时刻(NextRunTime/ScheduledTime/LeaseUntil/心跳)必须先过这里。
    /// 不是洁癖:MySQL <c>datetime(0)</c> 对毫秒四舍五入,内存 <c>.500</c> 入库进位到下一秒,
    /// 领取 CAS 的 <c>@expected</c> 从此永不命中,任务无声停摆(§13-9,变异测试锁死)。
    /// </summary>
    internal static DateTime Truncate(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, t.Kind);

    /// <summary>节点名:显式配置优先,空则 <c>{MachineName}#{WorkerId}</c>(§3.4)。</summary>
    internal static string ResolveNodeName(AdminJobsOptions jobs, AdminIdOptions id) =>
        string.IsNullOrWhiteSpace(jobs.NodeName) ? $"{Environment.MachineName}#{id.WorkerId ?? 0}" : jobs.NodeName!;
}
