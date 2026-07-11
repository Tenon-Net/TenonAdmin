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
        // 单元测试聚焦锁定计数本身,用固定策略桩喂阈值/时长(不牵扯 SysConfig/Options 解析)
        return new LoginLockService(cache, new FixedPolicy(maxFail, 10));
    }

    /// <summary>返回固定安全策略的极小桩(只喂 LoginLock 需要的阈值/时长)。</summary>
    private sealed class FixedPolicy(int maxFail, int lockMinutes) : ISecurityPolicyProvider
    {
        public Task<(int MaxFailCount, int LockMinutes)> GetLoginLockAsync() => Task.FromResult((maxFail, lockMinutes));
        public Task<(int AccessMinutes, int RefreshMinutes)> GetSessionTtlAsync() => Task.FromResult((120, 10080));
        public Task<PasswordPolicy> GetPasswordPolicyAsync() => Task.FromResult(new PasswordPolicy(8, true, true, true, false));
        public Task ValidatePasswordAsync(string password) => Task.CompletedTask;
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

    [Fact]
    public async Task Case_and_whitespace_variants_share_one_counter()
    {
        // P1-5:大小写/尾空白变体命中同一真实账号(大小写不敏感排序规则下),锁定计数不能被拆分绕过
        var s = Make(3);
        await s.RecordFailureAsync("admin");
        await s.RecordFailureAsync("ADMIN");
        await s.RecordFailureAsync(" Admin ");
        Assert.True(await IsLocked(s, "admin"));      // 三个变体累加达阈值
        Assert.True(await IsLocked(s, "AdMiN"));       // 任一变体查询都判锁定
    }

    [Fact]
    public async Task Concurrent_failures_all_count_no_lost_update()
    {
        // P2-11:并发失败计数走原子自增,不因读-改-写竞态丢失更新
        var s = Make(20);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => s.RecordFailureAsync("dave")));
        Assert.True(await IsLocked(s, "dave"));        // 20 次全部计入 → 达阈值
    }
}
