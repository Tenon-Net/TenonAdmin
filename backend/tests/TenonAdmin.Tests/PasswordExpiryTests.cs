using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 密码过期策略(dev-plan §5):运行时配置 <c>sys.security.password.expireDays</c>(默认 0=关闭)。
/// 过期<b>不拦登录</b>,仅置 MustChangePassword(与管理员重置同一信号)让前端强制跳转改密;
/// 存量用户(LastPasswordChangeTime=null)首次登录回填当前时间——功能启用当天不得全员被判过期。
/// </summary>
public class PasswordExpiryTests
{
    private const string Password = "Abcd1234";       // 满足默认复杂度(≥8、含大小写与数字)
    private const string NewPassword = "Efgh5678";

    /// <summary>建一个普通用户并把过期锚点摆成用例需要的样子(AddAsync 默认置强制改密,这里复位)。</summary>
    private static async Task<long> CreateUserAsync(IServiceProvider sp, string account, DateTime? lastChange)
    {
        var users = sp.GetRequiredService<IUserService>();
        var repo = sp.GetRequiredService<IRepository<SysUser>>();
        var id = (await users.AddAsync(new AddUserInput { Account = account, Name = account, Password = Password })).Id;
        var row = await repo.GetByIdAsync(id);
        row!.MustChangePassword = false;
        row.LastPasswordChangeTime = lastChange;
        await repo.UpdateAsync(row);
        return id;
    }

    private static Task SetExpireDaysAsync(IServiceProvider sp, int days) =>
        sp.GetRequiredService<IConfigService>().SaveValuesAsync(
            [new ConfigBatchItem { ConfigKey = "sys.security.password.expireDays", ConfigValue = days.ToString() }]);

    /// <summary>走真实 HTTP 登录(全管道),返回出参里的 mustChangePassword。</summary>
    private static async Task<bool> LoginAsync(HttpClient client, string account, string password = Password)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new { account, password });
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("mustChangePassword").GetBoolean();
    }

    [Fact]
    public async Task Expired_password_sets_persistent_must_change_flag()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        await SetExpireDaysAsync(scope.ServiceProvider, 30);
        var id = await CreateUserAsync(scope.ServiceProvider, "exp-old", DateTime.Now.AddDays(-60));

        Assert.True(await LoginAsync(factory.CreateClient(), "exp-old"));

        // 标志已持久化——后续刷新令牌换发同样带出,不是登录出参上的一次性字段
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        Assert.True((await repo.GetByIdAsync(id))!.MustChangePassword);
    }

    [Fact]
    public async Task Fresh_password_within_window_not_flagged()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        await SetExpireDaysAsync(scope.ServiceProvider, 30);
        await CreateUserAsync(scope.ServiceProvider, "exp-fresh", DateTime.Now.AddDays(-1));

        Assert.False(await LoginAsync(factory.CreateClient(), "exp-fresh"));
    }

    [Fact]
    public async Task Null_anchor_backfills_on_first_login_instead_of_expiring()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        await SetExpireDaysAsync(scope.ServiceProvider, 30);
        var id = await CreateUserAsync(scope.ServiceProvider, "exp-legacy", lastChange: null);

        Assert.False(await LoginAsync(factory.CreateClient(), "exp-legacy"));   // 存量用户不被判过期

        // 回填生效:过期窗口从这次登录起算
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        Assert.NotNull((await repo.GetByIdAsync(id))!.LastPasswordChangeTime);
    }

    [Fact]
    public async Task Disabled_policy_ignores_stale_anchor()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        // 不设配置 = 种子默认 0(永不过期):十年没改密也不置标志
        await CreateUserAsync(scope.ServiceProvider, "exp-off", DateTime.Now.AddDays(-3650));

        Assert.False(await LoginAsync(factory.CreateClient(), "exp-off"));
    }

    [Fact]
    public async Task Self_change_resets_expiry_window()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        await SetExpireDaysAsync(scope.ServiceProvider, 30);
        var id = await CreateUserAsync(scope.ServiceProvider, "exp-cycle", DateTime.Now.AddDays(-60));

        var client = factory.CreateClient();
        Assert.True(await LoginAsync(client, "exp-cycle"));   // 过期 → 强制改密

        var personal = scope.ServiceProvider.GetRequiredService<IPersonalService>();
        await personal.ChangePasswordAsync(id, new ChangePasswordInput { OldPassword = Password, NewPassword = NewPassword });

        Assert.False(await LoginAsync(client, "exp-cycle", NewPassword));   // 改密后窗口重新起算,标志已清
    }

    /// <summary>
    /// 过期判定读的是<b>注入的 TimeProvider</b>而非挂钟(§1.11,J1 收敛后的接缝证明)。
    /// 把时钟钉在 2030、锚点摆在 2029-11(距注入时钟已过 60 天,但相对真实"现在"却在未来):
    /// 唯有读注入时钟才判过期;若仍读 <c>DateTime.Now</c>,锚点在未来、绝不会过期——本用例即为该退化的探针。
    /// </summary>
    [Fact]
    public async Task Expiry_decision_follows_injected_clock_not_wallclock()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var factory = new AdminAppFactory { Overrides = s => s.AddSingleton<TimeProvider>(clock) };
        using var scope = factory.Services.CreateScope();
        await SetExpireDaysAsync(scope.ServiceProvider, 30);
        var id = await CreateUserAsync(scope.ServiceProvider, "exp-clock", new DateTime(2029, 11, 2));

        Assert.True(await LoginAsync(factory.CreateClient(), "exp-clock"));   // 注入时钟看来已过期 → 置强制改密

        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        Assert.True((await repo.GetByIdAsync(id))!.MustChangePassword);
    }
}

/// <summary>固定时钟(本地时区=UTC,免 TZ 偏移),只为证明过期判定走注入时钟。仓库约定不引 FakeTimeProvider 包,自写最小实现。</summary>
file sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
