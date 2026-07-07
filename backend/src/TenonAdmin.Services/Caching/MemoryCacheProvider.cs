using Microsoft.Extensions.Caching.Memory;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ICacheProvider"/> 默认实现:进程内 <see cref="IMemoryCache"/>(设计 §5.5)。
/// <para>单实例部署够用、零外部依赖;多实例共享缓存装 <c>TenonAdmin.Caching.Redis</c> 可选包前置替换。
/// 逻辑键统一追加 <see cref="AdminCacheOptions.KeyPrefix"/> 前缀,便于共享实例时按前缀隔离。</para>
/// <para>进程内缓存直接存对象引用、不序列化;故 <c>T</c> 原样进出(Redis 实现才涉及序列化)。</para>
/// </summary>
public class MemoryCacheProvider(IMemoryCache cache, AdminCacheOptions options) : ICacheProvider
{
    // ponytail: 单进程全局锁,只护原子取删/自增两条低频路径(登录失败计数、验证码消费)。
    // 若这两条成为热点再改条带锁(按 key 哈希分桶),当前无此压力。
    private readonly object _atomicLock = new();

    private string Prefixed(string key) => options.KeyPrefix + key;

    /// <inheritdoc />
    public virtual Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(cache.TryGetValue(Prefixed(key), out var value) ? (T?)value : default);

    /// <inheritdoc />
    public virtual Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        // expiry 为 null → 永不过期(只靠显式失效);给定 → 相对当前时刻的绝对过期
        if (expiry is { } ttl && ttl > TimeSpan.Zero)
            cache.Set(Prefixed(key), value, ttl);
        else
            cache.Set(Prefixed(key), value);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cache.Remove(Prefixed(key));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>进程内锁保证读-改-写原子,并发失败计数不丢更新。</remarks>
    public virtual Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var full = Prefixed(key);
        lock (_atomicLock)
        {
            var next = (cache.TryGetValue(full, out var v) ? Convert.ToInt64(v) : 0L) + 1;
            if (expiry is { } ttl && ttl > TimeSpan.Zero) cache.Set(full, next, ttl);
            else cache.Set(full, next);
            return Task.FromResult(next);
        }
    }

    /// <inheritdoc />
    /// <remarks>进程内锁保证取值与移除原子,并发下同一票据只有一个调用取得非空值。</remarks>
    public virtual Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var full = Prefixed(key);
        lock (_atomicLock)
        {
            var value = cache.TryGetValue(full, out var v) ? (T?)v : default;
            cache.Remove(full);
            return Task.FromResult(value);
        }
    }
}
