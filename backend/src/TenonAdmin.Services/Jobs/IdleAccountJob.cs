using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 内置狗粮任务:Level3 闲置账号扫描(60d 告警 / 90d 停用)。
/// 非 Level3 时服务内空跑。可在任务中心挂 cron 调度。
/// </summary>
public class IdleAccountJob(IIdleAccountService idle) : IAdminJob
{
    /// <inheritdoc />
    public virtual async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await idle.ScanAsync(cancellationToken);
        context.Log?.Invoke(
            $"闲置账号扫描:告警 {result.Warned},停用 {result.Disabled},超管仅告警 {result.SuperAdminWarnedOnly}");
    }
}
