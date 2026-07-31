using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 历史：Level3 关键依赖挂在 <c>/health/ready</c>，失败则 Unhealthy。
/// ADR 0006：可选安全不得拖垮就绪探针；始终 Healthy，保留注册以免破坏既有探针名。
/// 后续瘦身可整类删除。
/// </summary>
internal sealed class Level3PrecheckHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy(
            "Level3 ready 预检已退役(ADR 0006)；不再影响 /health/ready"));
}
