using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// Cookie 会话 + CSRF + 绝对/闲置/并发钳制(历史 Profile=Level3 兼容路径)。
/// 集成宿主用通过型预检桩;Redis 预检失败见 <see cref="SecurityBaselinePrecheckTests"/>。
/// </summary>
public class CookieSessionCsrfTests
{
    private static string DataProtectionKey => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Cookie/历史 Level3 工厂:通过型预检桩 + 数据保护密钥。</summary>
    private static AdminAppFactory Level3Factory(Action<IServiceCollection>? extra = null) => new()
    {
        Settings = new Dictionary<string, string?>
        {
            ["TenonAdmin:Security:Profile"] = "Level3",
            ["TenonAdmin:Security:DataProtection:Key"] = DataProtectionKey,
            // 会话 TTL 显式给足,便于绝对窗测试用 FakeTime 推进
            ["TenonAdmin:Jwt:ExpireMinutes"] = "15",
            ["TenonAdmin:Jwt:RefreshExpireMinutes"] = "480",
        },
        Overrides = services =>
        {
            // 用通过型预检替换,避免 Memory 缓存触发启动拒绝(真实 Redis 失败仍由 SecurityBaselinePrecheckTests 覆盖)
            services.RemoveAll<ISecurityBaselinePrecheckService>();
            services.AddSingleton<ISecurityBaselinePrecheckService, PassSecurityBaselinePrecheck>();
            // 会话/Cookie 用例专注会话层:关闭 MFA 强制,避免超管未绑 TOTP 挡登录
            services.RemoveAll<IMfaPolicyService>();
            services.AddSingleton<IMfaPolicyService, NoMfaPolicy>();
            // Level3 验证码下限强制开启时,测试用直通验证码避免登录被 40002 挡住
            services.RemoveAll<ICaptchaService>();
            services.AddSingleton<ICaptchaService, PassthroughCaptcha>();
            extra?.Invoke(services);
        },
    };

    private sealed class PassthroughCaptcha : ICaptchaService
    {
        public Task<CaptchaOutput> IssueAsync() =>
            Task.FromResult(new CaptchaOutput { CaptchaId = "t", Svg = "<svg/>", Type = "char" });

        public Task ValidateAsync(string? captchaId, string? code) => Task.CompletedTask;
    }

    private sealed class PassSecurityBaselinePrecheck : ISecurityBaselinePrecheckService
    {
        public Task<SecurityBaselinePrecheckResult> RunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecurityBaselinePrecheckResult
            {
                CapabilityVersion = SecurityBaselinePrecheckConstants.CapabilityVersion,
                Profile = "Level3",
                Environment = "Development",
                Checks = [],
                UnimplementedMandates = SecurityBaselinePrecheckService.UnimplementedMandates,
                OverallCompliantForPhase1 = true,
            });
    }

    private sealed class NoMfaPolicy : IMfaPolicyService
    {
        public Task<bool> IsTotpFeatureEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsMfaRequiredAsync(SysUser user, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlySet<string>> GetEffectiveHighSensitivityPermissionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<bool> HoldsHighSensitivityPermissionAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private static (string? Rt, string? Csrf) ParseAuthCookies(HttpResponseMessage resp)
    {
        string? rt = null, csrf = null;
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values))
            return (null, null);
        foreach (var raw in values)
        {
            if (raw.StartsWith(AuthCookieNames.RefreshToken + "=", StringComparison.OrdinalIgnoreCase))
                rt = CookieValue(raw, AuthCookieNames.RefreshToken);
            else if (raw.StartsWith(AuthCookieNames.Csrf + "=", StringComparison.OrdinalIgnoreCase))
                csrf = CookieValue(raw, AuthCookieNames.Csrf);
        }
        return (rt, csrf);
    }

    private static string? CookieValue(string setCookie, string name)
    {
        var m = Regex.Match(setCookie, $"^{Regex.Escape(name)}=([^;]+)", RegexOptions.IgnoreCase);
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
    }

    private static bool SetCookieHasFlags(HttpResponseMessage resp, string cookieName, params string[] flags)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values)) return false;
        var line = values.FirstOrDefault(v => v.StartsWith(cookieName + "=", StringComparison.OrdinalIgnoreCase));
        if (line is null) return false;
        return flags.All(f => line.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Level3_login_sets_secure_httponly_refresh_cookie_and_omits_body_refresh()
    {
        using var f = Level3Factory();
        var c = f.CreateClient();
        var resp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var j = await resp.ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        var data = j.GetProperty("data");
        Assert.False(string.IsNullOrEmpty(data.GetProperty("accessToken").GetString()));
        // body 清空 refresh
        Assert.True(
            !data.TryGetProperty("refreshToken", out var rtEl)
            || string.IsNullOrEmpty(rtEl.GetString()));

        Assert.True(SetCookieHasFlags(resp, AuthCookieNames.RefreshToken, "httponly", "secure", "samesite=lax"));
        Assert.True(SetCookieHasFlags(resp, AuthCookieNames.Csrf, "secure", "samesite=lax"));
        // CSRF 可读(无 HttpOnly)
        if (resp.Headers.TryGetValues("Set-Cookie", out var vals))
        {
            var csrfLine = vals.First(v => v.StartsWith(AuthCookieNames.Csrf + "=", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("httponly", csrfLine, StringComparison.OrdinalIgnoreCase);
        }

        var (rt, csrf) = ParseAuthCookies(resp);
        Assert.False(string.IsNullOrEmpty(rt));
        Assert.False(string.IsNullOrEmpty(csrf));
    }

    [Fact]
    public async Task Non_level3_login_keeps_body_refresh_without_auth_cookies()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        var resp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var j = await resp.ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("data").GetProperty("refreshToken").GetString()));

        if (resp.Headers.TryGetValues("Set-Cookie", out var vals))
        {
            Assert.DoesNotContain(vals, v => v.StartsWith(AuthCookieNames.RefreshToken + "=", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(vals, v => v.StartsWith(AuthCookieNames.Csrf + "=", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Level3_csrf_reject_without_header_and_pass_with_match()
    {
        using var f = Level3Factory();
        var c = f.CreateClient();
        var loginResp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var login = await loginResp.ReadEnvelope();
        Assert.Equal(0, login.GetProperty("code").GetInt32());
        var (rt, csrf) = ParseAuthCookies(loginResp);
        Assert.False(string.IsNullOrEmpty(rt));
        Assert.False(string.IsNullOrEmpty(csrf));
        var access = login.GetProperty("data").GetProperty("accessToken").GetString()!;

        // 无 CSRF 头 + 带 refresh cookie → 写操作 403/40023
        var bad = f.CreateClient();
        bad.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        bad.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        var denied = await bad.PostJson("/api/v1/auth/logout", new { });
        var deniedBody = await denied.ReadEnvelope();
        Assert.Equal((int)ErrorCode.CsrfInvalid, deniedBody.GetProperty("code").GetInt32());

        // TOTP 完成登录同样是状态改变 POST:有 refresh Cookie 时缺 CSRF 必须拒(防 raw fetch 绕过中间件)
        var totpNoCsrf = f.CreateClient();
        totpNoCsrf.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        var totpDenied = await totpNoCsrf.PostJson("/api/v1/auth/login/totp",
            new { challengeId = "x", code = "000000" });
        var totpDeniedBody = await totpDenied.ReadEnvelope();
        Assert.Equal((int)ErrorCode.CsrfInvalid, totpDeniedBody.GetProperty("code").GetInt32());

        // 匹配 CSRF → 通过
        var good = f.CreateClient();
        good.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        good.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        good.DefaultRequestHeaders.TryAddWithoutValidation(AuthCookieNames.CsrfHeader, csrf);
        var ok = await (await good.PostJson("/api/v1/auth/logout", new { })).ReadEnvelope();
        Assert.Equal(0, ok.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Level3_clear_cookies_reuse_CookieDomain_and_samesite_none()
    {
        // HttpClient CookieContainer 不接受 domain=.example.com 于 localhost URI,故用 HttpContext 直接断言
        var security = new AdminSecurityOptions
        {
            Profile = SecurityProfile.Level3,
            Level3 = new AdminLevel3Options { CookieDomain = ".example.com" },
        };
        var cookies = new AuthCookieService(security, new StubHostEnv());
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        cookies.ClearAuthCookies(http);

        Assert.True(http.Response.Headers.TryGetValue("Set-Cookie", out var setCookies));
        var lines = setCookies.ToString();
        // 删除响应须带与创建时相同的 Domain / Secure / SameSite
        Assert.Contains("domain=.example.com", lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AuthCookieNames.RefreshToken, lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AuthCookieNames.Csrf, lines, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHostEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public async Task Level3_refresh_from_cookie_when_body_empty()
    {
        using var f = Level3Factory();
        var c = f.CreateClient();
        var loginResp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var login = await loginResp.ReadEnvelope();
        var (rt, csrf) = ParseAuthCookies(loginResp);
        Assert.False(string.IsNullOrEmpty(rt));

        var refreshClient = f.CreateClient();
        refreshClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        refreshClient.DefaultRequestHeaders.TryAddWithoutValidation(AuthCookieNames.CsrfHeader, csrf!);
        // body 空 refreshToken
        var resp = await refreshClient.PostJson("/api/v1/auth/refresh", new { });
        var j = await resp.ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("data").GetProperty("accessToken").GetString()));
        // 仍不在 body 下发 refresh
        var data = j.GetProperty("data");
        Assert.True(!data.TryGetProperty("refreshToken", out var rtEl) || string.IsNullOrEmpty(rtEl.GetString()));
        // 新 Cookie 轮换
        var (rt2, csrf2) = ParseAuthCookies(resp);
        Assert.False(string.IsNullOrEmpty(rt2));
        Assert.NotEqual(rt, rt2);
        Assert.False(string.IsNullOrEmpty(csrf2));
    }

    [Fact]
    public async Task Level3_absolute_expiry_and_idle_enforced_on_session()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var f = Level3Factory(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

        // 直接驱动 SessionService:开会话 → 推进绝对窗 → IsActive false
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenProvider>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(user);
        var sid = Guid.CreateVersion7().ToString("N");
        var pair = tokens.Create(
            new TokenSubject(user!.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(8));
        await sessions.OpenAsync(user, sid, pair);

        var row = await sessionRepo.GetFirstAsync(s => s.SessionId == sid);
        Assert.NotNull(row);
        // 绝对窗 ≤ now+8h
        Assert.NotNull(row!.AbsoluteExpiresAt);
        Assert.True(row.AbsoluteExpiresAt.Value <= clock.GetUtcNow().UtcDateTime.AddHours(8).AddSeconds(2));
        Assert.True(await sessions.IsActiveAsync(sid));

        // 推进超过绝对窗
        clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
        Assert.False(await sessions.IsActiveAsync(sid));
    }

    [Fact]
    public async Task Level3_idle_timeout_uses_mfa_15_minutes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var f = Level3Factory(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenProvider>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        user!.TotpEnabled = true;
        await users.UpdateAsync(user);

        var sid = Guid.CreateVersion7().ToString("N");
        var pair = tokens.Create(
            new TokenSubject(user.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
        await sessions.OpenAsync(user, sid, pair);

        // 清缓存并回写 LastActivityAt 为 now,避免 IsActive 的 Touch 刷新活动时间干扰
        await cache.RemoveAsync(CacheKeys.Session(sid));
        var openAt = clock.GetUtcNow().UtcDateTime;
        await sessionRepo.Db.Updateable<SysSession>()
            .SetColumns(s => s.LastActivityAt == openAt)
            .Where(s => s.SessionId == sid)
            .ExecuteCommandAsync();

        // 推进 16 分钟(> MFA idle 15)且不经 IsActive Touch
        clock.Advance(TimeSpan.FromMinutes(16));
        await cache.RemoveAsync(CacheKeys.Session(sid));
        Assert.False(await sessions.IsActiveAsync(sid));
    }

    /// <summary>
    /// 部署把 IdleMinutesMfa/Normal 放宽到 120 时,有效闲置仍钳到 15/30 下限
    /// (Math.Min against Level3IdleMinutes*);16 分钟后 MFA 会话必须失效。
    /// </summary>
    [Fact]
    public async Task Level3_idle_floors_cannot_be_relaxed_by_config()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Profile"] = "Level3",
                ["TenonAdmin:Security:DataProtection:Key"] = DataProtectionKey,
                ["TenonAdmin:Jwt:ExpireMinutes"] = "15",
                ["TenonAdmin:Jwt:RefreshExpireMinutes"] = "480",
                // 故意放宽超过内核下限——有效策略必须仍为 15/30
                ["TenonAdmin:Security:Session:IdleMinutesMfa"] = "120",
                ["TenonAdmin:Security:Session:IdleMinutesNormal"] = "120",
            },
            Overrides = services =>
            {
                services.RemoveAll<ISecurityBaselinePrecheckService>();
                services.AddSingleton<ISecurityBaselinePrecheckService, PassSecurityBaselinePrecheck>();
                services.RemoveAll<IMfaPolicyService>();
                services.AddSingleton<IMfaPolicyService, NoMfaPolicy>();
                services.RemoveAll<ICaptchaService>();
                services.AddSingleton<ICaptchaService, PassthroughCaptcha>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            },
        };

        using var scope = f.Services.CreateScope();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();
        Assert.Equal(120, security.Session.IdleMinutesMfa);
        Assert.Equal(120, security.Session.IdleMinutesNormal);

        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenProvider>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        user!.TotpEnabled = true;
        await users.UpdateAsync(user);

        var sid = Guid.CreateVersion7().ToString("N");
        var pair = tokens.Create(
            new TokenSubject(user.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
        await sessions.OpenAsync(user, sid, pair);

        // 缓存中有效闲置必须是 15(不是配置的 120)
        var cached = await cache.GetAsync<SessionCacheInfo>(CacheKeys.Session(sid));
        Assert.NotNull(cached);
        Assert.Equal(SecurityPolicyProvider.Level3IdleMinutesMfa, cached!.IdleMinutes);

        await cache.RemoveAsync(CacheKeys.Session(sid));
        var openAt = clock.GetUtcNow().UtcDateTime;
        await sessionRepo.Db.Updateable<SysSession>()
            .SetColumns(s => s.LastActivityAt == openAt)
            .Where(s => s.SessionId == sid)
            .ExecuteCommandAsync();

        // 16 分钟 > 15 下限,若错误使用 120 配置则仍会活跃——必须失效
        clock.Advance(TimeSpan.FromMinutes(16));
        await cache.RemoveAsync(CacheKeys.Session(sid));
        Assert.False(await sessions.IsActiveAsync(sid));

        // 普通用户:配置 120 时有效仍为 30;推进 31 分钟后失效
        user.TotpEnabled = false;
        user.ForceTotp = false;
        await users.UpdateAsync(user);
        clock.Advance(TimeSpan.FromMinutes(-16)); // 回到 openAt 附近再开
        // 重新锚定时钟:直接设为当前假时钟,开新会话
        var sid2 = Guid.CreateVersion7().ToString("N");
        var pair2 = tokens.Create(
            new TokenSubject(user.Id, user.Account, sid2, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
        await sessions.OpenAsync(user, sid2, pair2);
        var cached2 = await cache.GetAsync<SessionCacheInfo>(CacheKeys.Session(sid2));
        Assert.Equal(SecurityPolicyProvider.Level3IdleMinutesNormal, cached2!.IdleMinutes);

        await cache.RemoveAsync(CacheKeys.Session(sid2));
        var open2 = clock.GetUtcNow().UtcDateTime;
        await sessionRepo.Db.Updateable<SysSession>()
            .SetColumns(s => s.LastActivityAt == open2)
            .Where(s => s.SessionId == sid2)
            .ExecuteCommandAsync();
        clock.Advance(TimeSpan.FromMinutes(31));
        await cache.RemoveAsync(CacheKeys.Session(sid2));
        Assert.False(await sessions.IsActiveAsync(sid2));
    }

    [Fact]
    public async Task Level3_concurrency_clamps_normal_to_3_and_mfa_to_1()
    {
        using var f = Level3Factory();
        // 默认 MaxConcurrent=0 → Level3 普通=3, MFA=1
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenProvider>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(user);
        user!.TotpEnabled = false;
        user.ForceTotp = false;
        await users.UpdateAsync(user);

        async Task OpenOne()
        {
            var sid = Guid.CreateVersion7().ToString("N");
            var pair = tokens.Create(
                new TokenSubject(user.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
                TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
            await sessions.OpenAsync(user, sid, pair);
        }

        for (var i = 0; i < 5; i++) await OpenOne();
        var now = DateTime.UtcNow;
        var active = await sessionRepo.AsQueryable()
            .Where(s => s.UserId == user.Id && s.RevokedAt == null && s.ExpiresAt > now)
            .ToListAsync();
        Assert.Equal(SecurityPolicyProvider.Level3MaxConcurrentNormal, active.Count);

        // 切 MFA → 再登一次应收敛到 1
        user.TotpEnabled = true;
        await users.UpdateAsync(user);
        await OpenOne();
        active = await sessionRepo.AsQueryable()
            .Where(s => s.UserId == user.Id && s.RevokedAt == null && s.ExpiresAt > now)
            .ToListAsync();
        Assert.Single(active); // MFA default keep=1
    }

    [Fact]
    public async Task Successful_login_updates_last_successful_login_at()
    {
        using var f = Level3Factory();
        var c = f.CreateClient();
        await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });

        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(user!.LastSuccessfulLoginAt);
    }
}

/// <summary>可控时钟(BCL FakeTimeProvider 在 net10 可用)。</summary>
file sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utc;

    public FakeTimeProvider(DateTimeOffset start) => _utc = start;

    public override DateTimeOffset GetUtcNow() => _utc;

    public void Advance(TimeSpan delta) => _utc += delta;
}
