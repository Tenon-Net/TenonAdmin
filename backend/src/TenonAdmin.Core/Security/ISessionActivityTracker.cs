namespace TenonAdmin.Core;

/// <summary>
/// 会话活动追踪(Level3 闲置判定的热路径)。
/// 每请求更新最近活动时间,经缓存节流后回写 DB——禁止每请求落库。
/// <para>Level3 下缓存写失败应失败关闭(调用方视 <c>false</c> 为会话失活),不得静默跳过闲置判定。</para>
/// </summary>
public interface ISessionActivityTracker
{
    /// <summary>
    /// 记录一次活动。成功返回 true;Level3 下缓存/回写关键路径失败返回 false(fail-closed)。
    /// 非 Level3 失败时仍返回 true(尽力而为,不阻断既有会话)。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="userId">用户 Id(回写会话行用)</param>
    /// <param name="sessionExpiresAt">会话过期时刻(刷新缓存 TTL)</param>
    Task<bool> TouchAsync(string sessionId, long userId, DateTime sessionExpiresAt, CancellationToken cancellationToken = default);
}
