using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOpenAppRateLimiter"/> 默认实现:一分钟固定窗口,窗口序号编进键(换窗口即换键、计数自然归零,旧键靠 TTL 回收)。
/// 与内核 <c>RateLimitMiddleware</c> 同一计数原语与取舍(窗口边界允许 2× 突发)。
/// </summary>
public class CacheOpenAppRateLimiter(ICacheProvider cache, IntegrationOptions options, TimeProvider time) : IOpenAppRateLimiter
{
    /// <summary>窗口长度(秒)。</summary>
    protected const int WindowSeconds = 60;

    /// <inheritdoc />
    public virtual async Task<OpenAppRateDecision> AcquireAsync(long appId, int limitPerMinute, CancellationToken cancellationToken = default)
    {
        if (limitPerMinute <= 0) return OpenAppRateDecision.Allow;
        var (window, retryAfter) = Window();
        var count = await cache.IncrementAsync($"itg:rl:app:{appId}:{window}", TimeSpan.FromSeconds(WindowSeconds), cancellationToken);
        return count <= limitPerMinute ? OpenAppRateDecision.Allow : new OpenAppRateDecision(false, retryAfter);
    }

    /// <inheritdoc />
    public virtual async Task<OpenAppRateDecision> CheckAuthenticationAsync(string clientKey, CancellationToken cancellationToken = default)
    {
        var limit = options.OpenApi.AuthFailuresPerMinutePerIp;
        if (limit <= 0) return OpenAppRateDecision.Allow;
        var (window, retryAfter) = Window();
        var failures = await cache.GetAsync<long?>(AuthFailureKey(clientKey, window), cancellationToken) ?? 0;
        return failures < limit ? OpenAppRateDecision.Allow : new OpenAppRateDecision(false, retryAfter);
    }

    /// <inheritdoc />
    public virtual async Task RecordAuthenticationFailureAsync(string clientKey, CancellationToken cancellationToken = default)
    {
        if (options.OpenApi.AuthFailuresPerMinutePerIp <= 0) return;
        var (window, _) = Window();
        await cache.IncrementAsync(AuthFailureKey(clientKey, window), TimeSpan.FromSeconds(WindowSeconds), cancellationToken);
    }

    private (long Window, int RetryAfter) Window()
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        return (now / WindowSeconds, (int)(WindowSeconds - now % WindowSeconds));
    }

    private static string AuthFailureKey(string clientKey, long window) => $"itg:rl:authfail:{clientKey}:{window}";
}
