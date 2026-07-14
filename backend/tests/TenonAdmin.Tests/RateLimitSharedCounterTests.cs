using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 限流计数的<b>跨副本共享</b>(§14 / T-D3)。限流窗口计数若留在进程内,N 个副本 = N × 阈值:
/// 认证桶默认 20/min,两副本就成了 40/min——§14 的爆破基线被<b>静默</b>削半,没有任何报错。
/// <para>计数改走 <see cref="ICacheProvider.IncrementAsync"/>(其 XML 注释本就写着"用于登录失败计数等
/// 并发安全计数场景",Memory/Redis 两个实现都已覆写为真原子)。于是:装 Redis = 计数跨副本共享;
/// 不装 = 退回进程内,与今天行为一致。<b>不新增任何抽象</b>。</para>
/// <para>本用例用"两个 host 共用同一个 <see cref="ICacheProvider"/> 实例"模拟两个副本共用一个 Redis——
/// 这正是共享计数唯一需要成立的条件。</para>
/// </summary>
public class RateLimitSharedCounterTests
{
    private const int AUTH_PERMIT = 4;

    /// <summary>两个"副本"共用的缓存实例(= 生产里的同一个 Redis)。</summary>
    private static ICacheProvider SharedCache() =>
        new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), new AdminCacheOptions());

    /// <summary>一个开了限流、且把认证桶收紧到 <see cref="AUTH_PERMIT"/> 的"副本",共用给定缓存。</summary>
    private static AdminAppFactory Replica(ICacheProvider shared) => new()
    {
        Settings = new Dictionary<string, string?>
        {
            ["TenonAdmin:Security:RateLimit:Enabled"] = "true",
            ["TenonAdmin:Security:RateLimit:WindowSeconds"] = "60",
            ["TenonAdmin:Security:RateLimit:AuthPermitPerWindow"] = AUTH_PERMIT.ToString(),
        },
        Overrides = s => s.Replace(ServiceDescriptor.Singleton(shared)),
    };

    /// <summary>把两个副本的限流阈值都从 DB 收紧(DB 值优先于 Options),并确定性刷新快照。</summary>
    private static async Task TightenAsync(params AdminAppFactory[] replicas)
    {
        foreach (var f in replicas)
        {
            using var scope = f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IConfigService>().SaveValuesAsync(
            [
                new() { ConfigKey = AdminRateLimitOptions.KEY_ENABLED, ConfigValue = "true" },
                new() { ConfigKey = AdminRateLimitOptions.KEY_WINDOW, ConfigValue = "60" },
                new() { ConfigKey = AdminRateLimitOptions.KEY_AUTH_PERMIT, ConfigValue = AUTH_PERMIT.ToString() },
            ]);
            await f.Services.GetRequiredService<RuntimeRateLimit>().RefreshAsync();
        }
    }

    [Fact]
    public async Task Two_replicas_sharing_a_cache_hit_the_same_threshold()
    {
        var shared = SharedCache();
        using var a = Replica(shared);
        using var b = Replica(shared);
        await TightenAsync(a, b);

        // 负载均衡轮询:请求交替落到两个副本上
        HttpClient[] lb = [a.CreateClient(), b.CreateClient()];
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < AUTH_PERMIT + 2; i++)
            statuses.Add((await lb[i % 2].GetAsync("/api/v1/auth/captcha")).StatusCode);

        // 前 AUTH_PERMIT 次放行(不论落在哪个副本),之后一律 429 ——
        // 阈值是"整个集群 4 次",不是"每副本 4 次、合计 8 次"。
        Assert.All(statuses.Take(AUTH_PERMIT), s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.All(statuses.Skip(AUTH_PERMIT), s => Assert.Equal(HttpStatusCode.TooManyRequests, s));
    }

    [Fact]
    public async Task Separate_caches_still_count_separately()
    {
        // 反面钉子:没有共享缓存(= 没装 Redis)时,各副本各数各的——这是今天的行为,也是不装 Redis 时的已知天花板。
        // 它证明上一条测的确实是"共享计数",而不是别的什么东西碰巧让请求被拒。
        using var a = Replica(SharedCache());
        using var b = Replica(SharedCache());
        await TightenAsync(a, b);

        var ca = a.CreateClient();
        var cb = b.CreateClient();
        for (var i = 0; i < AUTH_PERMIT; i++) await ca.GetAsync("/api/v1/auth/captcha");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await ca.GetAsync("/api/v1/auth/captcha")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cb.GetAsync("/api/v1/auth/captcha")).StatusCode);   // 另一副本毫不知情
    }
}
