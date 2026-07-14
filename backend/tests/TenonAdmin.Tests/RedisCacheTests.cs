using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Caching.Redis;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// Redis 缓存可选包测试,分两类:
/// <list type="number">
/// <item>DI 装配(离线):provider 惰性连接,无需活跃 Redis——验证按配置接管 <see cref="ICacheProvider"/>、
/// 前置注册赢内核 TryAdd、缺连接串 fail-fast、Memory 时空操作。</item>
/// <item>契约集成:仅当环境变量 <c>TENON_TEST_REDIS</c> 指向可用 Redis 时运行(对照 <see cref="TestDb"/> 的
/// <c>TENON_TEST_*</c> 门控),覆盖 set/get、缺失键、remove、原子自增可按 long 读回、取删一次性。</item>
/// </list>
/// </summary>
public class RedisCacheTests
{
    // ── (1) DI 装配(离线,无需 Redis) ─────────────────────────────────

    private static IConfiguration Config(params (string key, string? val)[] kv) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(kv.Select(p => new KeyValuePair<string, string?>(p.key, p.val)))
            .Build();

    [Fact]
    public void ConfigProviderRedis_RegistersRedisProvider()
    {
        var services = new ServiceCollection();
        services.AddTenonAdminRedisCache(Config(
            ("TenonAdmin:Cache:Provider", "Redis"),
            ("TenonAdmin:Cache:RedisConnectionString", "localhost:6379")));   // 惰性:构造不连接
        using var sp = services.BuildServiceProvider();
        Assert.IsType<RedisCacheProvider>(sp.GetRequiredService<ICacheProvider>());
    }

    [Fact]
    public void ConfigProviderMemory_IsNoOp()
    {
        var services = new ServiceCollection();
        services.AddTenonAdminRedisCache(Config(("TenonAdmin:Cache:Provider", "Memory")));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ICacheProvider));   // 未接管,留给内核 Memory
    }

    [Fact]
    public void RedisRegisteredBeforeKernel_WinsTryAdd()
    {
        var services = new ServiceCollection();
        services.AddTenonAdminRedisCache("localhost:6379");              // 前置注册
        services.TryAddSingleton<ICacheProvider, MemoryCacheProvider>(); // 模拟内核后置 TryAdd
        using var sp = services.BuildServiceProvider();
        Assert.IsType<RedisCacheProvider>(sp.GetRequiredService<ICacheProvider>());   // Redis 胜出
    }

    [Fact]
    public void ProviderRedis_WithoutConnectionString_ThrowsFailFast()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddTenonAdminRedisCache(Config(("TenonAdmin:Cache:Provider", "Redis"))));   // 缺连接串
    }

    // ── (2) 契约集成(需 TENON_TEST_REDIS 指向可用 Redis) ─────────────

    private static string? RedisConn => Environment.GetEnvironmentVariable("TENON_TEST_REDIS");

    /// <summary>
    /// 无 Redis 时本地跳过(开发机不该被强制装 Redis),<b>但在 CI 里缺 Redis 直接判红</b>。
    /// <para>原来是无条件 <c>return</c> —— 于是这几条契约测试在 CI 里<b>一次都没真跑过</b>,却次次报绿:
    /// 套件看着"覆盖了 Redis",实际一个断言都没执行。而多副本的整个逃生舱(强退、权限失效、锁定计数跨副本)
    /// 全建立在这个包上。静默跳过 = 假绿,必须堵死。</para>
    /// <para>xUnit 2.x 没有动态 Skip(要么加第三方包,要么这样)——CI 侧的 redis 服务容器见 backend-ci.yml。</para>
    /// </summary>
    private static bool SkipWithoutRedis()
    {
        if (RedisConn is not null) return false;
        Assert.True(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null,
            "CI 必须提供 Redis(设 TENON_TEST_REDIS,见 backend-ci.yml 的 redis 服务容器):契约测试静默跳过等于没测。");
        return true;
    }

    private static RedisCacheProvider NewProvider() =>
        new(new AdminCacheOptions { Provider = "Redis", RedisConnectionString = RedisConn!, KeyPrefix = "tenontest:" });

    [Fact]
    public async Task SetGetRemove_RoundTrips()
    {
        if (SkipWithoutRedis()) return;
        using var cache = NewProvider();
        var key = "obj:" + Guid.NewGuid().ToString("N");
        var value = new[] { 1L, 2L, 3L };

        Assert.Null(await cache.GetAsync<long[]>(key));           // 缺失 → default
        await cache.SetAsync(key, value);
        Assert.Equal(value, await cache.GetAsync<long[]>(key));   // 往返一致
        await cache.RemoveAsync(key);
        Assert.Null(await cache.GetAsync<long[]>(key));           // 移除后 → null
    }

    [Fact]
    public async Task Increment_IsAtomicAndReadableAsLong()
    {
        if (SkipWithoutRedis()) return;
        using var cache = NewProvider();
        var key = "cnt:" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Equal(1, await cache.IncrementAsync(key, TimeSpan.FromMinutes(1)));
            Assert.Equal(2, await cache.IncrementAsync(key, TimeSpan.FromMinutes(1)));
            Assert.Equal(2, await cache.GetAsync<long>(key));   // INCR 存的整数按 long 读得回(JSON 数字互通)
        }
        finally { await cache.RemoveAsync(key); }
    }

    [Fact]
    public async Task GetAndRemove_ConsumesOnce()
    {
        if (SkipWithoutRedis()) return;
        using var cache = NewProvider();
        var key = "ticket:" + Guid.NewGuid().ToString("N");
        await cache.SetAsync(key, "code123", TimeSpan.FromMinutes(2));
        Assert.Equal("code123", await cache.GetAndRemoveAsync<string>(key));   // 取到
        Assert.Null(await cache.GetAndRemoveAsync<string>(key));               // 已消费 → null
    }
}
