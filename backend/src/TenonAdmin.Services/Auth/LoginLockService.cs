using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ILoginLockService"/> 默认实现:失败计数存缓存,TTL = 锁定分钟数(滑动窗口)。
/// <para><c>MaxFailCount &lt;= 0</c> 视为关闭,所有方法变空操作。计数达阈值即认定锁定——
/// <see cref="EnsureNotLockedAsync"/> 在登录最前置调用,先于账密校验,锁定期间正确密码也进不来。</para>
/// </summary>
public class LoginLockService(ICacheProvider cache, AdminSecurityOptions security) : ILoginLockService
{
    /// <summary>
    /// 账号规范化(去首尾空白 + 转小写)——锁定计数的 key 等价类必须≥数据库匹配的等价类,
    /// 否则大小写/尾空白变体(MySQL utf8mb4_0900_ai_ci / PAD SPACE 命中同一行)会拆成独立计数器,
    /// 让攻击者绕过锁定无限猜测(设计 §14)。三个方法共用同一规范形,失败计数与成功重置对得上。
    /// </summary>
    private static string Normalize(string account) => account.Trim().ToLowerInvariant();

    /// <inheritdoc />
    public virtual async Task EnsureNotLockedAsync(string account)
    {
        var opt = security.LoginLock;
        if (opt.MaxFailCount <= 0) return;   // 功能关闭

        // 缓存未命中时 GetAsync<long> 返回 default(0)——值类型 T? 即 T,无需 ?? 兜底
        var count = await cache.GetAsync<long>(CacheKeys.LoginFail(Normalize(account)));
        AdminException.ThrowIf(count >= opt.MaxFailCount, ErrorCode.AccountLocked,
            new Dictionary<string, object?> { ["lockMinutes"] = opt.LockMinutes });
    }

    /// <inheritdoc />
    public virtual async Task RecordFailureAsync(string account)
    {
        var opt = security.LoginLock;
        if (opt.MaxFailCount <= 0) return;

        // 原子自增(并发失败不丢计数),同时刷新 TTL:持续失败 → 窗口顺延;停手 LockMinutes → 计数过期自动解锁
        await cache.IncrementAsync(CacheKeys.LoginFail(Normalize(account)), TimeSpan.FromMinutes(opt.LockMinutes));
    }

    /// <inheritdoc />
    public virtual Task ResetAsync(string account) => cache.RemoveAsync(CacheKeys.LoginFail(Normalize(account)));
}
