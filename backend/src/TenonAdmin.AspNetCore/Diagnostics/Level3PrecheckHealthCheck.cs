using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// Level3 关键依赖就绪探针:挂在 <c>/health/ready</c>。
/// 非 Level3 → Healthy;Level3 且预检关键项失败 → Unhealthy(带稳定 check id,无密钥)。
/// <para>经 <see cref="IServiceScopeFactory"/> 解析 Scoped 的 <see cref="ILevel3PrecheckService"/>
/// (健康检查本体为 Singleton,不能直接构造注入 Scoped 服务)。</para>
/// </summary>
internal sealed class Level3PrecheckHealthCheck(
    IServiceScopeFactory scopeFactory,
    ISecurityProfileAccessor profile) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!profile.IsLevel3)
            return HealthCheckResult.Healthy("Security Profile 非 Level3");

        using var scope = scopeFactory.CreateScope();
        var precheck = scope.ServiceProvider.GetRequiredService<ILevel3PrecheckService>();
        var result = await precheck.RunAsync(cancellationToken);
        if (!result.HasCriticalFailures)
            return HealthCheckResult.Healthy(
                $"Level3 预检通过(capability={result.CapabilityVersion})");

        var data = new Dictionary<string, object>
        {
            ["capabilityVersion"] = result.CapabilityVersion,
            ["criticalFailureIds"] = result.CriticalFailureIds.ToArray(),
            ["profile"] = result.Profile,
        };

        return HealthCheckResult.Unhealthy(
            $"Level3 关键预检失败: {string.Join(", ", result.CriticalFailureIds)}",
            data: data);
    }
}
