using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 磁盘回收的<b>单副本租约</b>(T-D3)。GC 后台任务在每个副本上都注册着,不设租约则 N 个副本
/// 同时扫同一批文件——逐行 try/catch + <c>File.Exists</c> 兜底使它<b>不会出错</b>,但白白重复 I/O
/// 并刷一堆"跳过文件"的告警。
/// <para>租约就是一次原子自增:时间桶做键,只有拿到 1 的副本干活。内存缓存(单实例)恒为 1 → 行为不变。</para>
/// <para>只测 <see cref="FileGcService.TryAcquireTickAsync"/>,它只碰 cache/options/time 三个依赖,
/// 其余构造参数传 null 是安全的(也让这条用例不必拖起一个库)。</para>
/// </summary>
public class FileGcTickLeaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>可推进的假时钟(用来跨到下一个时间桶)。</summary>
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private static ICacheProvider SharedCache() =>
        new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), new AdminCacheOptions());

    /// <summary>一个"副本"的 GC 服务。scopeFactory/storage/chunks 本用例用不到(见类注释)。</summary>
    private static FileGcService Replica(ICacheProvider? cache, TimeProvider clock) =>
        new(null!, null!, null!,
            new AdminUploadOptions { GcIntervalHours = 6 },
            clock,
            NullLogger<FileGcService>.Instance,
            cache);

    [Fact]
    public async Task Only_one_replica_takes_a_given_tick()
    {
        var shared = SharedCache();
        var clock = new FakeClock(T0);
        var a = Replica(shared, clock);
        var b = Replica(shared, clock);

        Assert.True(await a.TryAcquireTickAsync());    // 先到者领走这一轮
        Assert.False(await b.TryAcquireTickAsync());   // 另一个副本跳过,不重复扫盘
    }

    [Fact]
    public async Task The_next_tick_is_up_for_grabs_again()
    {
        var shared = SharedCache();
        var clock = new FakeClock(T0);
        var a = Replica(shared, clock);
        var b = Replica(shared, clock);

        Assert.True(await a.TryAcquireTickAsync());
        Assert.False(await b.TryAcquireTickAsync());

        clock.Advance(TimeSpan.FromHours(6));          // 下一个时间桶 → 新键 → 重新开抢
        Assert.True(await b.TryAcquireTickAsync());    // 这轮轮到 b(不是永远锁死在 a 身上)
        Assert.False(await a.TryAcquireTickAsync());
    }

    [Fact]
    public async Task Without_a_cache_every_tick_is_taken()
    {
        // 消费者手工 new 出来的实例(没有缓存)→ 退回"人人都扫",与加租约之前的行为一致
        var gc = Replica(cache: null, new FakeClock(T0));

        Assert.True(await gc.TryAcquireTickAsync());
        Assert.True(await gc.TryAcquireTickAsync());
    }
}
