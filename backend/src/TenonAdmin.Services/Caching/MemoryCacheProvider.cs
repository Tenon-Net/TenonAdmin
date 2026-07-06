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
}
