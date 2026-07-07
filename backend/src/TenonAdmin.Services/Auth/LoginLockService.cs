using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ILoginLockService"/> 默认实现:失败计数存缓存,TTL = 锁定分钟数(滑动窗口)。
/// <para><c>MaxFailCount &lt;= 0</c> 视为关闭,所有方法变空操作。计数达阈值即认定锁定——
/// <see cref="EnsureNotLockedAsync"/> 在登录最前置调用,先于账密校验,锁定期间正确密码也进不来。</para>
/// </summary>
public class LoginLockService(ICacheProvider cache, AdminSecurityOptions security) : ILoginLockService
{
    /// <inheritdoc />
    public virtual async Task EnsureNotLockedAsync(string account)
    {
        var opt = security.LoginLock;
        if (opt.MaxFailCount <= 0) return;   // 功能关闭

        // 缓存未命中时 GetAsync<int> 返回 default(0)——值类型 T? 即 T,无需 ?? 兜底
        var count = await cache.GetAsync<int>(CacheKeys.LoginFail(account));
        AdminException.ThrowIf(count >= opt.MaxFailCount, ErrorCode.AccountLocked,
            new Dictionary<string, object?> { ["lockMinutes"] = opt.LockMinutes });
    }

    /// <inheritdoc />
    public virtual async Task RecordFailureAsync(string account)
    {
        var opt = security.LoginLock;
        if (opt.MaxFailCount <= 0) return;

        var key = CacheKeys.LoginFail(account);
        var count = await cache.GetAsync<int>(key) + 1;
        // 每次失败刷新 TTL:持续失败 → 窗口顺延;停手 LockMinutes → 计数过期、自动解锁
        await cache.SetAsync(key, count, TimeSpan.FromMinutes(opt.LockMinutes));
    }

    /// <inheritdoc />
    public virtual Task ResetAsync(string account) => cache.RemoveAsync(CacheKeys.LoginFail(account));
}
