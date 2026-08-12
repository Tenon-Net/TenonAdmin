using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 外部登录 / SSO(批次 D)行为锁。用一个假 <see cref="IExternalAuthProvider"/> 顶替真 IdP(不依赖网络),
/// 覆核心分支:未绑定默认拒绝(Q1)、开启自动开户则建号+绑定、已绑定则复用会话、未知 provider 拒、绑定唯一。
/// </summary>
public class ExternalAuthTests
{
    // 假 provider:ExchangeAsync 直接回一个已知外部身份;Code = 该身份的 provider 码。
    // 授权 URL 回声 state,便于 HTTP 级 authorize→callback 测试从 Location 取回。
    private sealed class FakeExternalAuthProvider(ExternalIdentity identity) : IExternalAuthProvider
    {
        public string Code => identity.Provider;
        public string DisplayName => "Fake";
        public string? Icon => null;
        public Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult($"https://idp.test/authorize?state={Uri.EscapeDataString(request.State)}");
        public Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(identity);
    }

    // 把 state 回声进授权 URL,好让 HTTP 级测试从 302 Location 取回 state 再打回调;身份恒未绑定(→ 40016)。
    private sealed class StateEchoProvider : IExternalAuthProvider
    {
        public string Code => "echo";
        public string DisplayName => "Echo";
        public string? Icon => null;
        public Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult($"https://idp.test/authorize?state={Uri.EscapeDataString(request.State)}");
        public Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalIdentity("echo", "sub-echo"));
    }

    private sealed class CancellationCapturingProvider(
        ExternalIdentity identity,
        Action<CancellationToken> capture) : IExternalAuthProvider
    {
        public string Code => identity.Provider;
        public string DisplayName => "Cancellation capture";
        public string? Icon => null;
        public Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult("https://idp.test/authorize");
        public Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
        {
            capture(cancellationToken);
            return Task.FromResult(identity);
        }
    }

    private static AdminAppFactory Factory(ExternalIdentity identity) => new()
    {
        Overrides = s => s.AddSingleton<IExternalAuthProvider>(new FakeExternalAuthProvider(identity)),
    };

    private static ExternalLoginInput Input(string providerCode) => new()
    {
        ProviderCode = providerCode,
        Code = "auth-code",
        CodeVerifier = "verifier",
        Nonce = "nonce",
        RedirectUri = "https://app/cb",
    };

    private static async Task<SysUser> InsertUserAsync(IServiceProvider sp, string account)
    {
        var user = new SysUser { Account = account, Password = sp.GetRequiredService<IPasswordHasher>().Hash("x"), Name = account, Enabled = true };
        await sp.GetRequiredService<IRepository<SysUser>>().InsertAsync(user);
        return user;
    }

    [Fact]
    public async Task Unbound_external_login_is_rejected_by_default()
    {
        var identity = new ExternalIdentity("test", "sub-reject", "U", "u@example.com");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var ex = await Assert.ThrowsAsync<AdminException>(() => auth.LoginByExternalAsync(Input("test")));
        Assert.Equal(ErrorCode.OAuthAccountNotBound, ex.Code);   // Q1 安全默认:未绑定 = 拒绝
    }

    [Fact]
    public async Task Unbound_external_login_provisions_when_configured()
    {
        var identity = new ExternalIdentity("prov", "sub-prov", "Prov User", "prov@example.com");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // 打开该 provider 的自动开户(运营配置走 sys_config)
        await sp.GetRequiredService<IConfigService>().AddAsync(new ConfigInput
        {
            ConfigKey = "sys.externalauth.prov.provisioning",
            ConfigValue = "provision",
            Name = "外部登录-prov-未绑定策略",
        });

        var output = await sp.GetRequiredService<IAuthService>().LoginByExternalAsync(Input("prov"));
        Assert.False(string.IsNullOrEmpty(output.AccessToken));

        // 建了绑定行,也建了本地用户(免改密、账号由外部身份派生)
        var binding = await sp.GetRequiredService<ISysUserExternalService>().FindByExternalAsync("prov", "sub-prov");
        Assert.NotNull(binding);
        Assert.Equal(output.UserId, binding!.UserId);
        var user = await sp.GetRequiredService<IRepository<SysUser>>().GetByIdAsync(binding.UserId);
        Assert.NotNull(user);
        Assert.False(user!.MustChangePassword);
        Assert.StartsWith("prov_", user.Account);
    }

    [Fact]
    public async Task Bound_external_login_reuses_the_bound_user()
    {
        var identity = new ExternalIdentity("test", "sub-bound");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var user = await InsertUserAsync(sp, "bound-user");
        await sp.GetRequiredService<ISysUserExternalService>().BindAsync(user.Id, identity);

        var output = await sp.GetRequiredService<IAuthService>().LoginByExternalAsync(Input("test"));
        Assert.Equal(user.Id, output.UserId);   // 复用已绑账号,不新建
    }

    [Fact]
    public async Task Unbind_then_external_login_is_rejected()
    {
        // 解绑后同一外部身份不得再直接 SSO 进系统(须 pending-link / 再绑)
        var identity = new ExternalIdentity("test", "sub-unbind");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var ext = sp.GetRequiredService<ISysUserExternalService>();
        var auth = sp.GetRequiredService<IAuthService>();

        var user = await InsertUserAsync(sp, "unbind-user");
        await ext.BindAsync(user.Id, identity);
        Assert.NotNull(await ext.FindByExternalAsync("test", "sub-unbind"));

        await ext.UnbindAsync(user.Id, "test");
        Assert.Null(await ext.FindByExternalAsync("test", "sub-unbind"));

        var ex = await Assert.ThrowsAsync<AdminException>(() => auth.LoginByExternalAsync(Input("test")));
        Assert.Equal(ErrorCode.OAuthAccountNotBound, ex.Code);
    }

    [Fact]
    public async Task Unbind_then_http_callback_issues_pending_link_not_ticket()
    {
        var identity = new ExternalIdentity("test", "sub-http-un");
        using var f = Factory(identity);
        var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        // 先绑再解
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var user = await InsertUserAsync(sp, "http-un");
            var ext = sp.GetRequiredService<ISysUserExternalService>();
            await ext.BindAsync(user.Id, identity);
            await ext.UnbindAsync(user.Id, "test");
        }

        var authz = await client.GetAsync("/api/v1/auth/external/test/authorize");
        var query = new Uri(authz.Headers.Location!.ToString()).Query.TrimStart('?');
        var state = Uri.UnescapeDataString(query.Split('&').First(p => p.StartsWith("state="))["state=".Length..]);
        var binder = authz.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("tn_oauth_state=")).Split(';')[0];

        var cbReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/auth/external/test/callback?code=c&state={Uri.EscapeDataString(state)}");
        cbReq.Headers.Add("Cookie", binder);
        var cb = await client.SendAsync(cbReq);
        var loc = cb.Headers.Location!.ToString();
        Assert.Contains("pendingLink=", loc);
        Assert.DoesNotContain("ticket=", loc);
    }

    [Fact]
    public async Task Deleting_a_user_frees_its_external_binding()
    {
        var identity = new ExternalIdentity("test", "sub-del");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var ext = sp.GetRequiredService<ISysUserExternalService>();

        var user = await InsertUserAsync(sp, "to-delete");
        await ext.BindAsync(user.Id, identity);
        Assert.NotNull(await ext.FindByExternalAsync("test", "sub-del"));

        await sp.GetRequiredService<IUserService>().DeleteAsync(user.Id);

        // QA23:软删保留外部绑定;唯一位仍被占用,不能直接重绑
        Assert.NotNull(await ext.FindByExternalAsync("test", "sub-del"));
        var other = await InsertUserAsync(sp, "rebinder");
        await Assert.ThrowsAsync<AdminException>(() => ext.BindAsync(other.Id, identity));

        // 回收站硬删路径会 UnbindAll → 释放 (Provider,Subject) 唯一位后可重绑
        await ext.UnbindAllAsync(user.Id);
        Assert.Null(await ext.FindByExternalAsync("test", "sub-del"));
        await ext.BindAsync(other.Id, identity);
        Assert.Equal(other.Id, (await ext.FindByExternalAsync("test", "sub-del"))!.UserId);
    }

    [Fact]
    public async Task Unknown_provider_is_rejected()
    {
        var identity = new ExternalIdentity("test", "x");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var ex = await Assert.ThrowsAsync<AdminException>(() => auth.LoginByExternalAsync(Input("does-not-exist")));
        Assert.Equal(ErrorCode.OAuthProviderDisabled, ex.Code);
    }

    [Fact]
    public async Task External_login_propagates_cancellation_token_to_provider()
    {
        var identity = new ExternalIdentity("cancel", "sub-cancel");
        var observed = CancellationToken.None;
        using var f = new AdminAppFactory
        {
            Overrides = s => s.AddSingleton<IExternalAuthProvider>(
                new CancellationCapturingProvider(identity, token => observed = token)),
        };
        using var scope = f.Services.CreateScope();
        using var cts = new CancellationTokenSource();

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            scope.ServiceProvider.GetRequiredService<IAuthService>()
                .LoginByExternalAsync(Input("cancel"), cts.Token));

        Assert.Equal(ErrorCode.OAuthAccountNotBound, ex.Code);
        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task Public_providers_omits_disabled_while_admin_list_includes_it()
    {
        var identity = new ExternalIdentity("test", "sub-en");
        using var f = Factory(identity);
        var anon = f.CreateClient();
        var admin = f.CreateClient();
        var token = await admin.LoginToken("superAdmin", "Test@123456");
        admin.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 默认启用 → 公开列表含 test
        var pubOn = await (await anon.GetAsync("/api/v1/auth/external/providers")).ReadEnvelope();
        Assert.Equal(0, pubOn.GetProperty("code").GetInt32());
        Assert.Contains(pubOn.GetProperty("data").EnumerateArray(), p => p.GetProperty("code").GetString() == "test");

        var allOn = await (await admin.GetAsync("/api/v1/auth/external/providers/all")).ReadEnvelope();
        Assert.Equal(0, allOn.GetProperty("code").GetInt32());
        Assert.Contains(allOn.GetProperty("data").EnumerateArray(),
            p => p.GetProperty("code").GetString() == "test" && p.GetProperty("enabled").GetBoolean());

        // 运营关闭(SaveValues 可对 externalauth 键自动建行)
        using (var scope = f.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IConfigService>().SaveValuesAsync(
            [
                new ConfigBatchItem { ConfigKey = "sys.externalauth.test.enabled", ConfigValue = "false" },
            ]);
        }

        var pubOff = await (await anon.GetAsync("/api/v1/auth/external/providers")).ReadEnvelope();
        Assert.DoesNotContain(pubOff.GetProperty("data").EnumerateArray(),
            p => p.GetProperty("code").GetString() == "test");

        var allOff = await (await admin.GetAsync("/api/v1/auth/external/providers/all")).ReadEnvelope();
        var row = Assert.Single(allOff.GetProperty("data").EnumerateArray(),
            p => p.GetProperty("code").GetString() == "test");
        Assert.False(row.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Concurrent_SaveValues_first_write_of_externalauth_key_does_not_throw()
    {
        // 两路同时首配同一 sys.externalauth.* 键:不得因唯一索引 500;最终值可读。
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var key = $"sys.externalauth.race-{Guid.NewGuid():N}.enabled";

        var t1 = cfg.SaveValuesAsync([new ConfigBatchItem { ConfigKey = key, ConfigValue = "true" }]);
        var t2 = cfg.SaveValuesAsync([new ConfigBatchItem { ConfigKey = key, ConfigValue = "false" }]);
        await Task.WhenAll(t1, t2);

        var v = await cfg.GetValueByKeyAsync(key);
        Assert.True(v is "true" or "false");
    }

    [Fact]
    public void LooksLikeUniqueKeyViolation_detects_common_messages()
    {
        Assert.True(ConfigService.LooksLikeUniqueKeyViolation(new Exception("UNIQUE constraint failed: sys_config.ConfigKey")));
        Assert.True(ConfigService.LooksLikeUniqueKeyViolation(new Exception("Duplicate entry 'x' for key 'PRIMARY'")));
        Assert.False(ConfigService.LooksLikeUniqueKeyViolation(new Exception("timeout waiting for lock")));
    }

    [Fact]
    public async Task Login_callback_requires_the_state_cookie_from_the_initiating_browser()
    {
        // HandleCookies=false:客户端不自动存/发 cookie,由测试精确控制"带不带 binder"(否则 authorize 的 Set-Cookie 会被自动回传)
        using var f = new AdminAppFactory { Overrides = s => s.AddSingleton<IExternalAuthProvider>(new StateEchoProvider()) };
        var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        // 发起 authorize → 取回 (state, binder cookie 名值对)
        async Task<(string State, string Cookie)> StartAsync()
        {
            var resp = await client.GetAsync("/api/v1/auth/external/echo/authorize");
            Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
            var query = new Uri(resp.Headers.Location!.ToString()).Query.TrimStart('?');
            var state = Uri.UnescapeDataString(query.Split('&').First(p => p.StartsWith("state=")).Substring("state=".Length));
            var cookie = resp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("tn_oauth_state=")).Split(';')[0];
            return (state, cookie);
        }

        // 1) 无 binder cookie 回调 → 拒(40014 OAuthStateInvalid):正是他人拼接 (code,state) 诱导登录的场景
        var (state1, _) = await StartAsync();
        var noCookie = await client.GetAsync($"/api/v1/auth/external/echo/callback?code=c&state={state1}");
        Assert.Contains($"error={(int)ErrorCode.OAuthStateInvalid}", noCookie.Headers.Location!.ToString());

        // 2) 带发起浏览器的 binder cookie 回调 → 过 CSRF 门,未绑定 reject 走 pending-link(现场绑定)而非死 40016
        var (state2, cookie2) = await StartAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/auth/external/echo/callback?code=c&state={state2}");
        req.Headers.Add("Cookie", cookie2);
        var withCookie = await client.SendAsync(req);
        var loc2 = withCookie.Headers.Location!.ToString();
        Assert.Contains("pendingLink=", loc2);
        Assert.Contains("provider=echo", loc2);
        Assert.DoesNotContain($"error={(int)ErrorCode.OAuthAccountNotBound}", loc2);
    }

    [Fact]
    public async Task Unbound_login_callback_pending_link_can_be_claimed_after_password_login()
    {
        var identity = new ExternalIdentity("test", "sub-pending", "Pending User");
        using var f = Factory(identity);
        var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        // 1) 未绑定 SSO → pendingLink + tn_oauth_pending binder cookie
        var authz = await client.GetAsync("/api/v1/auth/external/test/authorize");
        Assert.Equal(HttpStatusCode.Redirect, authz.StatusCode);
        var query = new Uri(authz.Headers.Location!.ToString()).Query.TrimStart('?');
        var state = Uri.UnescapeDataString(query.Split('&').First(p => p.StartsWith("state="))["state=".Length..]);
        var binderCookie = authz.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("tn_oauth_state=")).Split(';')[0];

        var cbReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/auth/external/test/callback?code=c&state={Uri.EscapeDataString(state)}");
        cbReq.Headers.Add("Cookie", binderCookie);
        var cb = await client.SendAsync(cbReq);
        var loc = cb.Headers.Location!.ToString();
        Assert.Contains("pendingLink=", loc);
        Assert.Contains("displayName=", loc); // 确认框展示用
        var pending = Uri.UnescapeDataString(
            new Uri(loc.Contains("://") ? loc : "http://x" + (loc.StartsWith('/') ? loc : "/" + loc))
                .Query.TrimStart('?')
                .Split('&')
                .First(p => p.StartsWith("pendingLink="))["pendingLink=".Length..]);
        var pendingCookie = cb.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("tn_oauth_pending=")).Split(';')[0];

        // 2) 无 binder cookie → 40014,且不消费票据(合法浏览器仍可再 claim)
        var token = await client.LoginToken("superAdmin", "Test@123456");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var claimBodyJson = System.Text.Json.JsonSerializer.Serialize(new { pendingLink = pending });
        var noCookieClaim = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/external/pending-link/claim")
        {
            Content = new StringContent(claimBodyJson, System.Text.Encoding.UTF8, "application/json"),
        };
        var noCookieResp = await client.SendAsync(noCookieClaim);
        var noCookieEnv = await noCookieResp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.OAuthStateInvalid, noCookieEnv.GetProperty("code").GetInt32());

        // 3) 带正确 binder → 绑定成功
        var okClaim = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/external/pending-link/claim")
        {
            Content = new StringContent(claimBodyJson, System.Text.Encoding.UTF8, "application/json"),
        };
        okClaim.Headers.Add("Cookie", pendingCookie);
        var claimResp = await client.SendAsync(okClaim);
        var claimEnv = await claimResp.ReadEnvelope();
        Assert.Equal(0, claimEnv.GetProperty("code").GetInt32());
        Assert.True(claimEnv.GetProperty("data").GetBoolean());

        using (var scope = f.Services.CreateScope())
        {
            var binding = await scope.ServiceProvider.GetRequiredService<ISysUserExternalService>()
                .FindByExternalAsync("test", "sub-pending");
            Assert.NotNull(binding);
        }

        // 4) 票据单次消费:再 claim 失败 40014
        var claim2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/external/pending-link/claim")
        {
            Content = new StringContent(claimBodyJson, System.Text.Encoding.UTF8, "application/json"),
        };
        claim2.Headers.Add("Cookie", pendingCookie);
        var claim2Resp = await client.SendAsync(claim2);
        var claim2Env = await claim2Resp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.OAuthStateInvalid, claim2Env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Claim_pending_link_without_binder_cookie_is_rejected()
    {
        var identity = new ExternalIdentity("test", "sub-nobinder");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var pending = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var binder = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await sp.GetRequiredService<ICacheProvider>().SetAsync(
            CacheKeys.OAuthPendingLink(pending),
            new ExternalPendingLinkPayload
            {
                Provider = identity.Provider,
                Subject = identity.Subject,
                DisplayName = "x",
                Binder = binder,
            },
            TimeSpan.FromMinutes(15));

        var client = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var token = await client.LoginToken("superAdmin", "Test@123456");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var claimResp = await client.PostAsync("/api/v1/auth/external/pending-link/claim", new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { pendingLink = pending }),
            System.Text.Encoding.UTF8,
            "application/json"));
        var env = await claimResp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.OAuthStateInvalid, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Claim_pending_link_conflicts_when_already_bound_to_other_user()
    {
        var identity = new ExternalIdentity("test", "sub-taken");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var other = await InsertUserAsync(sp, "owner");
        await sp.GetRequiredService<ISysUserExternalService>().BindAsync(other.Id, identity);

        // 手工塞一条 pending(含 binder);claim 带正确 cookie 仍因已被他人绑定 → 40017
        var pending = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var binder = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await sp.GetRequiredService<ICacheProvider>().SetAsync(
            CacheKeys.OAuthPendingLink(pending),
            new ExternalPendingLinkPayload
            {
                Provider = identity.Provider,
                Subject = identity.Subject,
                DisplayName = identity.DisplayName,
                Binder = binder,
            },
            TimeSpan.FromMinutes(15));

        var client = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var token = await client.LoginToken("superAdmin", "Test@123456");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var claimReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/external/pending-link/claim")
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { pendingLink = pending }),
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        claimReq.Headers.Add("Cookie", $"tn_oauth_pending={binder}");
        var claimResp = await client.SendAsync(claimReq);
        var env = await claimResp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.OAuthAlreadyBound, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Binding_same_external_identity_to_two_users_conflicts()
    {
        var identity = new ExternalIdentity("test", "sub-shared");
        using var f = Factory(identity);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ISysUserExternalService>();

        var a = await InsertUserAsync(sp, "user-a");
        var b = await InsertUserAsync(sp, "user-b");
        await svc.BindAsync(a.Id, identity);                       // 首绑成功
        await svc.BindAsync(a.Id, identity);                       // 幂等:重复绑到本人无副作用

        var ex = await Assert.ThrowsAsync<AdminException>(() => svc.BindAsync(b.Id, identity));
        Assert.Equal(ErrorCode.OAuthAlreadyBound, ex.Code);        // 该外部身份已归他人
    }

    /// <summary>
    /// 测试宿主:策略层 IsLevel3=true,但跳过启动 Redis 预检闸门(与 MfaEnrollmentTests 同款)。
    /// </summary>
    private static AdminAppFactory Level3MfaFactory(IExternalAuthProvider provider) => new()
    {
        Settings = new Dictionary<string, string?>
        {
            ["TenonAdmin:Security:DataProtection:Key"] =
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["TenonAdmin:Security:DataProtection:KeyVersion"] = "1",
        },
        Overrides = s =>
        {
            s.AddSingleton(provider);
            s.Replace(ServiceDescriptor.Singleton<ISecurityProfileAccessor, Level3ProfileStub>());
            foreach (var d in s.ToList())
            {
                if (d.ServiceType != typeof(IHostedService)) continue;
                var name = d.ImplementationType?.Name
                           ?? d.ImplementationInstance?.GetType().Name
                           ?? "";
                if (name.Contains("SecurityStartupDiagnostic", StringComparison.Ordinal))
                    s.Remove(d);
            }
        },
    };

    /// <summary>Level3 强制 MFA:外部登录已绑用户须 TOTP;未绑定 → TotpNotBound,不可直接出票。</summary>
    [Fact]
    public async Task Level3_external_login_unbound_force_totp_is_rejected()
    {
        var identity = new ExternalIdentity("sso-mfa", "sub-force");
        using var f = Level3MfaFactory(new FakeExternalAuthProvider(identity));
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var user = await InsertUserAsync(sp, "sso-force-user");
        user.ForceTotp = true;
        user.TotpEnabled = false;
        await users.UpdateAsync(user);
        await sp.GetRequiredService<ISysUserExternalService>().BindAsync(user.Id, identity);

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            sp.GetRequiredService<IAuthService>().LoginByExternalAsync(Input("sso-mfa")));
        Assert.Equal(ErrorCode.TotpNotBound, ex.Code);
    }

    /// <summary>Level3:外部登录已绑且已启用 TOTP → 40018 信令 + challengeId(供回调页完成流)。</summary>
    [Fact]
    public async Task Level3_external_login_totp_required_carries_challenge_id()
    {
        var identity = new ExternalIdentity("sso-totp", "sub-totp");
        using var f = Level3MfaFactory(new FakeExternalAuthProvider(identity));
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var totp = sp.GetRequiredService<ITotpService>();
        var protector = sp.GetRequiredService<ISecretProtector>();

        var user = await InsertUserAsync(sp, "sso-totp-user");
        user.ForceTotp = true;
        user.TotpEnabled = true;
        var seed = totp.GenerateSeed();
        user.TotpSeedProtected = protector.Protect(seed);
        user.TotpBoundAt = DateTime.Now;
        await users.UpdateAsync(user);
        await sp.GetRequiredService<ISysUserExternalService>().BindAsync(user.Id, identity);

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            sp.GetRequiredService<IAuthService>().LoginByExternalAsync(Input("sso-totp")));
        Assert.Equal(ErrorCode.TotpRequired, ex.Code);
        Assert.NotNull(ex.Args);
        Assert.True(ex.Args!.ContainsKey("challengeId"));
        Assert.False(string.IsNullOrEmpty(ex.Args["challengeId"]?.ToString()));

        // 凭挑战 + 正确码可完成登录(与密码路径同一完成端点)
        var login = await sp.GetRequiredService<IAuthService>().LoginByTotpChallengeAsync(new TotpChallengeLoginInput
        {
            ChallengeId = ex.Args["challengeId"]!.ToString()!,
            Code = totp.ComputeCode(seed),
        });
        Assert.Equal(user.Id, login.UserId);
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
    }

    private sealed class Level3ProfileStub : ISecurityProfileAccessor
    {
        public SecurityProfile Profile => SecurityProfile.Level3;
        public bool IsLevel3 => true;
        public bool IsProductionWithoutLevel3 => false;
    }
}
