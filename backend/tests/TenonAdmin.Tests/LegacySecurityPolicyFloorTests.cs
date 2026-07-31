using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 历史 Profile=Level3 策略下限:默认 Profile 不钳位;Level3 下 SysConfig 试图放宽仍被读取层钳制。
/// 直接驱动 <see cref="SecurityPolicyProvider"/> 真实现。
/// </summary>
public class LegacySecurityPolicyFloorTests
{
    private sealed class MapConfig(Dictionary<string, string?> map) : IConfigService
    {
        public Task<string?> GetValueByKeyAsync(string key) =>
            Task.FromResult(map.TryGetValue(key, out var v) ? v : null);

        public Task<PagedList<SysConfig>> PageAsync(ConfigPageInput input) => throw new NotImplementedException();
        public Task<SysConfig> GetAsync(long id) => throw new NotImplementedException();
        public Task<SiteInfoOutput> GetSiteInfoAsync() => throw new NotImplementedException();
        public Task SaveValuesAsync(IReadOnlyCollection<ConfigBatchItem> items) => throw new NotImplementedException();
        public Task<long> AddAsync(ConfigInput input) => throw new NotImplementedException();
        public Task UpdateAsync(long id, ConfigInput input) => throw new NotImplementedException();
        public Task DeleteAsync(long id) => throw new NotImplementedException();
    }

    private static SecurityPolicyProvider Make(
        SecurityProfile profile,
        Dictionary<string, string?>? cfg = null,
        AdminJwtOptions? jwt = null,
        AdminSecurityOptions? security = null)
    {
        security ??= new AdminSecurityOptions();
        security.Profile = profile;
        return new SecurityPolicyProvider(
            new MapConfig(cfg ?? new Dictionary<string, string?>()),
            security,
            jwt ?? new AdminJwtOptions());
    }

    [Fact]
    public async Task Default_profile_keeps_legacy_password_and_lock_defaults()
    {
        var p = Make(SecurityProfile.None);

        var policy = await p.GetPasswordPolicyAsync();
        Assert.Equal(8, policy.MinLength);
        Assert.True(policy.RequireUpper);
        Assert.True(policy.RequireLower);
        Assert.True(policy.RequireDigit);
        Assert.False(policy.RequireSpecial);

        Assert.Equal(0, await p.GetPasswordHistoryCountAsync());
        Assert.Equal(0, await p.GetPasswordExpireDaysAsync());

        var (maxFail, lockMin) = await p.GetLoginLockAsync();
        Assert.Equal(5, maxFail);
        Assert.Equal(10, lockMin); // Options 默认 LockMinutes=10,非 Level3 不抬

        // 旧默认复杂度:8 位 + 大小写数字即可
        await p.ValidatePasswordAsync("Abcd1234");
    }

    [Fact]
    public async Task Level3_clamps_min_length_when_sysconfig_tries_to_relax()
    {
        var p = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_MIN_LEN] = "6",
            [SecurityPolicyProvider.KEY_REQ_UPPER] = "false",
            [SecurityPolicyProvider.KEY_REQ_LOWER] = "false",
            [SecurityPolicyProvider.KEY_REQ_DIGIT] = "false",
            [SecurityPolicyProvider.KEY_REQ_SPECIAL] = "false",
        });

        var policy = await p.GetPasswordPolicyAsync();
        Assert.Equal(12, policy.MinLength);
        // 有效要求不足 3 类 → 抬到 upper+lower+digit
        Assert.True(policy.RequireUpper);
        Assert.True(policy.RequireLower);
        Assert.True(policy.RequireDigit);

        // 长度 6 的口令即使有三类字符也拒
        var err = await CatchCode(() => p.ValidatePasswordAsync("Abcdef1!"));
        Assert.Equal(ErrorCode.PasswordTooWeak, err);

        // 12 位 + 至少 3 类通过
        await p.ValidatePasswordAsync("Abcdefghij12");
    }

    [Fact]
    public async Task Level3_lock_minutes_floor_and_fail_cap()
    {
        // SysConfig 试图 1 分钟锁 / 10 次失败 → 仍强制 ≥15 分、≤5 次
        var p = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_MAX_FAIL] = "10",
            [SecurityPolicyProvider.KEY_LOCK_MIN] = "1",
        });

        var (maxFail, lockMin) = await p.GetLoginLockAsync();
        Assert.Equal(5, maxFail);
        Assert.Equal(15, lockMin);
    }

    [Fact]
    public async Task Level3_disabled_lock_is_forced_on()
    {
        var p = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_MAX_FAIL] = "0",
            [SecurityPolicyProvider.KEY_LOCK_MIN] = "0",
        });

        var (maxFail, lockMin) = await p.GetLoginLockAsync();
        Assert.Equal(5, maxFail);
        Assert.Equal(15, lockMin);
    }

    [Fact]
    public async Task Level3_history_and_expire_floors()
    {
        var p = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_HISTORY_COUNT] = "2",
            [SecurityPolicyProvider.KEY_EXPIRE_DAYS] = "0",
        });

        Assert.Equal(5, await p.GetPasswordHistoryCountAsync());
        Assert.Equal(90, await p.GetPasswordExpireDaysAsync());

        // 可收紧:expire 60 保留;history 10 保留
        var tight = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_HISTORY_COUNT] = "10",
            [SecurityPolicyProvider.KEY_EXPIRE_DAYS] = "60",
        });
        Assert.Equal(10, await tight.GetPasswordHistoryCountAsync());
        Assert.Equal(60, await tight.GetPasswordExpireDaysAsync());

        // expire >90 钳到 90
        var loose = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_EXPIRE_DAYS] = "180",
        });
        Assert.Equal(90, await loose.GetPasswordExpireDaysAsync());
    }

    [Fact]
    public async Task Level3_session_ttl_floors()
    {
        var p = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_ACCESS_MIN] = "120",
            [SecurityPolicyProvider.KEY_REFRESH_MIN] = "10080",
        }, jwt: new AdminJwtOptions { ExpireMinutes = 120, RefreshExpireMinutes = 10080 });

        var (access, refresh) = await p.GetSessionTtlAsync();
        Assert.Equal(15, access);
        Assert.Equal(SecurityPolicyProvider.Level3MaxAbsoluteSessionMinutes, refresh);
    }

    [Fact]
    public async Task Level3_allows_tighter_session_ttl()
    {
        var p = Make(SecurityProfile.Level3, new Dictionary<string, string?>
        {
            [SecurityPolicyProvider.KEY_ACCESS_MIN] = "5",
            [SecurityPolicyProvider.KEY_REFRESH_MIN] = "60",
        });

        var (access, refresh) = await p.GetSessionTtlAsync();
        Assert.Equal(5, access);
        Assert.Equal(60, refresh);
    }

    [Fact]
    public void Security_profile_accessor_reports_level3()
    {
        var env = new FakeHostEnv(isProduction: false);
        var acc = new SecurityProfileAccessor(
            new AdminSecurityOptions { Profile = SecurityProfile.Level3 }, env);
        Assert.True(acc.IsLevel3);
        Assert.False(acc.IsProductionWithoutLevel3);

        var prodNone = new SecurityProfileAccessor(
            new AdminSecurityOptions { Profile = SecurityProfile.None },
            new FakeHostEnv(isProduction: true));
        Assert.True(prodNone.IsProductionWithoutLevel3);
    }

    private static async Task<ErrorCode?> CatchCode(Func<Task> a)
    {
        try { await a(); return null; }
        catch (AdminException e) { return e.Code; }
    }

    private sealed class FakeHostEnv(bool isProduction) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = isProduction ? "Production" : "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
