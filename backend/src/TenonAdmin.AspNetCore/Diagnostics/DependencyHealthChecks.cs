using Microsoft.Extensions.Diagnostics.HealthChecks;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 数据库就绪探针(设计 §12 <c>/health/ready</c>):对配置的库跑一次轻量查询,连不上即 Unhealthy。
/// </summary>
internal sealed class DatabaseHealthCheck(ISqlSugarClient db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Ado.GetScalarAsync("select 1");
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("数据库不可达", ex);
        }
    }
}

/// <summary>
/// 缓存就绪探针(设计 §12 <c>/health/ready</c>):写读一个探针键往返,失败即 Unhealthy。
/// </summary>
internal sealed class CacheHealthCheck(ICacheProvider cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            const string key = "health:ready:probe";
            await cache.SetAsync(key, 1, TimeSpan.FromSeconds(5), cancellationToken);
            var ok = await cache.GetAsync<int>(key, cancellationToken) == 1;
            return ok ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("缓存读写往返失败");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("缓存不可达", ex);
        }
    }
}
