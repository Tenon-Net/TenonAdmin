namespace TenonAdmin.Integration;

/// <summary>一次限流判定。</summary>
/// <param name="Allowed">是否放行</param>
/// <param name="RetryAfterSeconds">拒绝时建议的重试间隔(秒)</param>
public readonly record struct OpenAppRateDecision(bool Allowed, int RetryAfterSeconds)
{
    /// <summary>放行。</summary>
    public static OpenAppRateDecision Allow => new(true, 0);
}

/// <summary>
/// 开放接口限流(实现契约 §9):按接入应用的每分钟调用上限,以及认证失败上限(保护未认证入口;计数分区由认证处理器给出:
/// 格式正确的凭据为来源 IP + 凭据标识,其余为来源 IP)。
/// <para>默认实现 <see cref="CacheOpenAppRateLimiter"/> 为固定窗口,计数走 <c>ICacheProvider.IncrementAsync</c>:
/// 装 Redis 即全集群共享计数;未装时每副本独立计数(N 副本约为 N 倍阈值,与内核 IP 限流一致)。前置注册同接口即可替换。</para>
/// </summary>
public interface IOpenAppRateLimiter
{
    /// <summary>为应用消费一次调用额度;<paramref name="limitPerMinute"/> ≤ 0 表示不限。</summary>
    Task<OpenAppRateDecision> AcquireAsync(long appId, int limitPerMinute, CancellationToken cancellationToken = default);

    /// <summary>该计数分区当前是否因认证失败过多而被暂时拒绝(在查库前调用)。</summary>
    Task<OpenAppRateDecision> CheckAuthenticationAsync(string clientKey, CancellationToken cancellationToken = default);

    /// <summary>为该计数分区记一次认证失败。</summary>
    Task RecordAuthenticationFailureAsync(string clientKey, CancellationToken cancellationToken = default);
}
