using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;

namespace TenonAdmin.Caching.Redis;

/// <summary>
/// Redis 缓存装配(可选包入口)。在 <c>AddTenonAdmin()</c> <b>之前</b>调用即前置注册 <see cref="ICacheProvider"/>
/// 的 Redis 实现,压过内核默认的进程内 <c>MemoryCacheProvider</c>(内核用 <c>TryAdd</c> 注册,先到者胜,设计 §5.2)。
/// <para>缓存落 Redis 后,现有"变更即 <c>RemoveAsync</c> 失效"逻辑天然跨实例生效(共享键空间),多实例部署无需额外改动。</para>
/// </summary>
public static class RedisSetup
{
    /// <summary>
    /// 按配置启用 Redis 缓存:读 <c>TenonAdmin:Cache</c> 节,仅当 <c>Provider=Redis</c> 时注册 <see cref="RedisCacheProvider"/>;
    /// 否则空操作(保留内核 Memory 默认),给消费方"改 appsettings 一处开关"的体验。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddTenonAdminRedisCache(builder.Configuration); // 先注册,赢 TryAdd
    /// builder.Services.AddTenonAdmin(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddTenonAdminRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("TenonAdmin:Cache").Get<AdminCacheOptions>() ?? new AdminCacheOptions();
        if (!string.Equals(options.Provider, "Redis", StringComparison.OrdinalIgnoreCase))
            return services;   // 未选 Redis:不接管,内核 Memory 默认生效
        return Register(services, options);
    }

    /// <summary>
    /// 显式用给定连接串启用 Redis 缓存(代码侧,不看配置的 <c>Provider</c> 开关——调用即启用)。
    /// </summary>
    public static IServiceCollection AddTenonAdminRedisCache(
        this IServiceCollection services, string connectionString, string keyPrefix = "tenon:") =>
        Register(services, new AdminCacheOptions { Provider = "Redis", RedisConnectionString = connectionString, KeyPrefix = keyPrefix });

    private static IServiceCollection Register(IServiceCollection services, AdminCacheOptions options)
    {
        // 装配期即校验连接串(fail-fast),而非等首次用缓存才在工厂里抛
        if (string.IsNullOrWhiteSpace(options.RedisConnectionString))
            throw new InvalidOperationException("启用 Redis 缓存需配置 TenonAdmin:Cache:RedisConnectionString(或经重载显式传入连接串)。");

        // TryAdd:与内核可替换性模型一致——本方法须在 AddTenonAdmin() 之前调用方能胜出。
        services.TryAddSingleton<ICacheProvider>(_ => new RedisCacheProvider(options));
        return services;
    }
}
