namespace TenonAdmin.Core;

/// <summary>
/// 缓存提供者(扩展点,设计 §5.5)。用户权限码 / 菜单 / 字典 / 配置等热数据进缓存,变更时显式失效。
/// <para>默认实现 <c>MemoryCacheProvider</c>(进程内 <c>IMemoryCache</c>);
/// 装可选包 <c>TenonAdmin.Caching.Redis</c> 后前置注册 <c>RedisCacheProvider</c> 即整体替换,
/// 换成分布式缓存、多实例共享。</para>
/// <para>约定:传入的是<b>逻辑键</b>(如 <c>perm:123</c>,见 <see cref="CacheKeys"/>);
/// 实现统一追加 <c>Cache:KeyPrefix</c> 前缀(共享 Redis 实例时按前缀隔离命名空间),调用方不关心前缀。</para>
/// </summary>
public interface ICacheProvider
{
    /// <summary>取值;键不存在返回 <c>default</c>(引用类型即 null)。已缓存的空集合与"未缓存"因此可区分。</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>写值。<paramref name="expiry"/> 为 null 时永不过期(仅靠显式失效清除);给定即相对当前时刻的过期时长。</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>移除键(权限/授权变更后主动失效对应缓存)。</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子自增并返回新值(不存在按 0 起算),<paramref name="expiry"/> 为写入时的过期时长。
    /// 用于登录失败计数等并发安全计数场景(设计 §14)。
    /// <para>默认实现是<b>非原子</b>的读-改-写兜底(并发下可能丢失更新);
    /// 需要真原子性的 provider(内存/Redis)应覆写:内存用锁、Redis 用 <c>INCR</c>+<c>EXPIRE</c>。</para>
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => IncrementFallbackAsync(key, expiry, cancellationToken);

    /// <summary>
    /// 原子取值并移除(get-and-delete):返回当前值后立即删除该键,并发下同一键只有一个调用能取到值。
    /// 用于验证码等一次性票据消费(设计 §14,杜绝并发重放)。
    /// <para>默认实现是<b>非原子</b>的先读后删兜底;provider 应覆写为原子操作(内存用锁、Redis 用 <c>GETDEL</c>)。</para>
    /// </summary>
    Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default)
        => GetAndRemoveFallbackAsync<T>(key, cancellationToken);

    /// <summary>默认非原子自增兜底(供未覆写的实现复用)。</summary>
    protected async Task<long> IncrementFallbackAsync(string key, TimeSpan? expiry, CancellationToken cancellationToken)
    {
        var next = await GetAsync<long>(key, cancellationToken) + 1;
        await SetAsync(key, next, expiry, cancellationToken);
        return next;
    }

    /// <summary>默认非原子取删兜底(供未覆写的实现复用)。</summary>
    protected async Task<T?> GetAndRemoveFallbackAsync<T>(string key, CancellationToken cancellationToken)
    {
        var value = await GetAsync<T>(key, cancellationToken);
        await RemoveAsync(key, cancellationToken);
        return value;
    }
}
