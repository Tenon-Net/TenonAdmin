using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 多副本共用同一个雪花机器号 = 撞主键(§12 / T-D3)。这是本批唯一一个<b>数据损坏级</b>的问题,
/// 而它今天<b>静默</b>发生:<c>docker compose up --scale app=2</c> 起来的两个副本都拿 <c>WorkerId=0</c>,
/// 同毫秒发号即冲突(主键冲突 / 数据错插),没有任何征兆。
///
/// <para>没有可靠的"我是不是多副本"信号,但有一个足够好的<b>意图</b>信号:<b>配了 Redis 缓存</b>——
/// 进程内缓存对多副本根本不成立(强退、权限失效、锁定计数全废),所以选 Redis 基本等同于宣告"我要多实例"。
/// 于是:<c>Cache:Provider=Redis</c> 且没有<b>显式</b>给 WorkerId → <b>启动即抛</b>,
/// 把一个静默的数据损坏换成一条可读的启动错误(同 <c>JwtKeyResolver</c> 生产缺密钥的成法)。</para>
///
/// <para><b>不做</b>从 hostname 哈希自动分配:6 bit 只有 64 个槽,2 副本就有约 1.6%、4 副本约 9% 的碰撞概率——
/// 在一个以"正确性"为主题的改动里,静默 1.6% 撞主键是不能接受的。k8s 用 StatefulSet 的序号注入。</para>
/// </summary>
public class WorkerIdGuardTests
{
    private static AdminAppFactory Factory(params (string key, string? value)[] settings) =>
        new() { Settings = settings.ToDictionary(s => s.key, s => s.value) };

    [Fact]
    public void Redis_cache_without_an_explicit_worker_id_fails_fast()
    {
        using var f = Factory(
            ("TenonAdmin:Cache:Provider", "Redis"),
            ("TenonAdmin:Cache:RedisConnectionString", "localhost:6379"));

        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("WorkerId", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redis_cache_with_an_explicit_worker_id_starts()
    {
        // 显式给了机器号(哪怕就是 0)= 运维知道自己在干什么 → 放行
        using var f = Factory(
            ("TenonAdmin:Cache:Provider", "Redis"),
            ("TenonAdmin:Cache:RedisConnectionString", "localhost:6379"),
            ("TenonAdmin:Id:WorkerId", "0"));

        _ = f.CreateClient();
        Assert.Equal(0, f.Services.GetRequiredService<AdminIdOptions>().WorkerId);
    }

    [Fact]
    public void Memory_cache_without_a_worker_id_still_starts()
    {
        // 零配置单实例:不配 WorkerId 照常跑(回落 0),三行启动的体验不受影响
        using var f = new AdminAppFactory();

        _ = f.CreateClient();
        Assert.Null(f.Services.GetRequiredService<AdminIdOptions>().WorkerId);
    }
}
