using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISessionActivityTracker"/> 默认实现:更新会话缓存中的 LastActivityAt,
/// 并按 <see cref="AdminSessionOptions.ActivityThrottleSeconds"/> 节流回写 DB。
/// 启用闲置/Cookie 会话时缓存异常 → 返回 false(活动不可信);否则吞掉并返回 true。
/// </summary>
public class SessionActivityTracker(
    ICacheProvider cache,
    IRepository<SysSession> sessions,
    AdminSecurityOptions security,
    TimeProvider time) : ISessionActivityTracker
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public virtual async Task<bool> TouchAsync(
        string sessionId, long userId, DateTime sessionExpiresAt, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = Now;
            var key = CacheKeys.Session(sessionId);
            var info = await cache.GetAsync<SessionCacheInfo>(key, cancellationToken);
            if (info is not null)
            {
                var ttl = sessionExpiresAt - now;
                if (ttl > TimeSpan.Zero)
                {
                    await cache.SetAsync(key, info with { LastActivityAt = now }, ttl, cancellationToken);
                }
            }

            var throttleSec = security.Session.ActivityThrottleSeconds > 0
                ? security.Session.ActivityThrottleSeconds
                : 60;
            var throttleKey = CacheKeys.SessionActivityThrottle(sessionId);
            var lastWrite = await cache.GetAsync<DateTime?>(throttleKey, cancellationToken);
            if (lastWrite is null || (now - lastWrite.Value).TotalSeconds >= throttleSec)
            {
                await sessions.Db.Updateable<SysSession>()
                    .SetColumns(s => s.LastActivityAt == now)
                    .Where(s => s.SessionId == sessionId)
                    .ExecuteCommandAsync();
                await cache.SetAsync(throttleKey, now, TimeSpan.FromSeconds(throttleSec * 2), cancellationToken);
            }

            return true;
        }
        catch
        {
            // 闲置或 Cookie 会话依赖活动跟踪:失败则 fail-closed;否则不阻断
            var strict = security.IsSessionIdleEnabled || security.IsCookieSessionEnabled;
            return !strict;
        }
    }
}
