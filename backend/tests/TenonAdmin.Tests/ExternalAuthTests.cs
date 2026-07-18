using Microsoft.Extensions.DependencyInjection;
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
    private sealed class FakeExternalAuthProvider(ExternalIdentity identity) : IExternalAuthProvider
    {
        public string Code => identity.Provider;
        public string DisplayName => "Fake";
        public string? Icon => null;
        public Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult("https://idp.test/authorize");
        public Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(identity);
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
}
