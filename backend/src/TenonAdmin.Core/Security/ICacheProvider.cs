namespace TenonAdmin.Core;

/// <summary>
/// 缓存提供者(扩展点,设计 §5.5)。用户权限码 / 菜单 / 字典 / 配置等热数据进缓存,变更时显式失效。
/// <para>默认实现 <c>MemoryCacheProvider</c>(进程内 <c>IMemoryCache</c>);
/// 装可选包 <c>TenonAdmin.Caching.Redis</c> 后前置注册 <c>RedisCacheProvider</c> 即整体替换,
/// 换成分布式缓存、多实例共享。</para>
/// <para>约定:传入的是<b>逻辑键</b>(如 <c>perm:123:v2</c>,见 <see cref="CacheKeys"/>);
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
    /// <para>提供者必须覆写为原子操作(包括过期设置)；默认实现拒绝执行，避免安全计数静默退化。</para>
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => IncrementFallbackAsync(key, expiry, cancellationToken);

    /// <summary>
    /// 原子取值并移除(get-and-delete):返回当前值后立即删除该键,并发下同一键只有一个调用能取到值。
    /// 用于验证码等一次性票据消费(设计 §14,杜绝并发重放)。
    /// <para>提供者必须覆写为原子操作(内存用锁、Redis 用 <c>GETDEL</c>)；默认实现拒绝执行。</para>
    /// </summary>
    Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default)
        => GetAndRemoveFallbackAsync<T>(key, cancellationToken);

    /// <summary>保留兼容入口，但禁止以非原子读写模拟安全计数。</summary>
    protected Task<long> IncrementFallbackAsync(string key, TimeSpan? expiry, CancellationToken cancellationToken)
        => Task.FromException<long>(new NotSupportedException("ICacheProvider must implement atomic IncrementAsync."));

    /// <summary>保留兼容入口，但禁止以非原子读删模拟一次性票据消费。</summary>
    protected Task<T?> GetAndRemoveFallbackAsync<T>(string key, CancellationToken cancellationToken)
        => Task.FromException<T?>(new NotSupportedException("ICacheProvider must implement atomic GetAndRemoveAsync."));
}
