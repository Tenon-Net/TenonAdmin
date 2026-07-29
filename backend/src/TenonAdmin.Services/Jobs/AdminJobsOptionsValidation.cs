using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="AdminJobsOptions"/> 的启动期校验——API 宿主与独立 Worker 共用同一入口,
/// 避免 Worker 漏掉 CIDR 围栏校验或数值选项 fail-fast 时与 API 行为分叉。
/// </summary>
public static class AdminJobsOptionsValidation
{
    /// <summary>校验 Jobs 选项;任一不合法即抛 <see cref="InvalidOperationException"/>,拒绝启动。</summary>
    public static void Validate(AdminJobsOptions jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        if (jobs.HeartbeatSeconds <= 0)
            throw new InvalidOperationException(
                $"TenonAdmin:Jobs 配置无效:HeartbeatSeconds({jobs.HeartbeatSeconds})必须为正数。");
        if (jobs.ReloadSeconds <= 0)
            throw new InvalidOperationException(
                $"TenonAdmin:Jobs 配置无效:ReloadSeconds({jobs.ReloadSeconds})必须为正数。");
        if (jobs.MisfireThresholdSeconds <= 0)
            throw new InvalidOperationException(
                $"TenonAdmin:Jobs 配置无效:MisfireThresholdSeconds({jobs.MisfireThresholdSeconds})必须为正数。");
        if (jobs.MaxConcurrentRuns <= 0)
            throw new InvalidOperationException(
                $"TenonAdmin:Jobs 配置无效:MaxConcurrentRuns({jobs.MaxConcurrentRuns})必须为正数。");
        // 租约必须容得下两次心跳丢失,否则一次 GC 停顿/DB 抖动就丢主,主备来回震荡。
        if (jobs.LeaseSeconds <= jobs.HeartbeatSeconds * 2)
            throw new InvalidOperationException(
                $"TenonAdmin:Jobs 配置无效:LeaseSeconds({jobs.LeaseSeconds})必须大于 2×HeartbeatSeconds({jobs.HeartbeatSeconds})。");
        if (jobs.Http.MaxResponseLogBytes < 0)
            throw new InvalidOperationException(
                $"TenonAdmin:Jobs:Http 配置无效:MaxResponseLogBytes({jobs.Http.MaxResponseLogBytes})不能为负数。");

        // 围栏条目写错会静默变空集(整条黑名单失效、启动日志一个字不提)——这是失效开,代价不对称,故绑定期就抛
        foreach (var cidr in jobs.Http.BlockedCidrs)
        {
            if (!JobHttpFence.TryParseCidr(cidr, out _, out _))
                throw new InvalidOperationException(
                    $"TenonAdmin:Jobs:Http:BlockedCidrs 配置无效:\"{cidr}\" 不是合法的 CIDR 或 IP 地址(写错会让 SSRF 围栏静默失效)。");
        }
    }
}
