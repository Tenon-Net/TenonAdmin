using Microsoft.Extensions.Caching.Memory;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>登录失败锁定(§14)——由 scratchpad t8b-loginlock-check 转正。</summary>
public class LoginLockServiceTests
{
    private static LoginLockService Make(int maxFail)
    {
        var cache = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), new AdminCacheOptions());
        var security = new AdminSecurityOptions { LoginLock = new AdminLoginLockOptions { MaxFailCount = maxFail, LockMinutes = 10 } };
        return new LoginLockService(cache, security);
    }

    private static async Task<bool> IsLocked(LoginLockService s, string account)
    {
        try { await s.EnsureNotLockedAsync(account); return false; }
        catch (AdminException e) when (e.Code == ErrorCode.AccountLocked) { return true; }
    }

    [Fact]
    public async Task Locks_after_threshold_and_isolates_accounts()
    {
        var s = Make(3);
        Assert.False(await IsLocked(s, "bob"));

        await s.RecordFailureAsync("bob");
        await s.RecordFailureAsync("bob");
        Assert.False(await IsLocked(s, "bob"));   // 2 < 3

        await s.RecordFailureAsync("bob");
        Assert.True(await IsLocked(s, "bob"));     // 达阈值
        Assert.False(await IsLocked(s, "alice"));  // 账号隔离
    }

    [Fact]
    public async Task Reset_clears_lock()
    {
        var s = Make(1);
        await s.RecordFailureAsync("bob");
        Assert.True(await IsLocked(s, "bob"));
        await s.ResetAsync("bob");
        Assert.False(await IsLocked(s, "bob"));
    }

    [Fact]
    public async Task Disabled_never_locks()
    {
        var s = Make(0);
        for (var i = 0; i < 20; i++) await s.RecordFailureAsync("carol");
        Assert.False(await IsLocked(s, "carol"));
    }
}
