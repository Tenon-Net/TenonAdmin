using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// TOTP 运行时总闸(与 CaptchaConfigTests 同款):默认关时 ForceTotp 不拦登录;
/// 经 config/batch 开启后强制对象登录返回 40020(未绑定)。
/// </summary>
public class TotpConfigTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Force_totp_ignored_when_runtime_and_options_off()
    {
        await using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Totp:Enabled"] = "false",
                ["TenonAdmin:Security:DataProtection:Key"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            },
        };

        using (var scope = f.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            await users.InsertAsync(new SysUser
            {
                Account = "force_off_" + Guid.NewGuid().ToString("N")[..8],
                Password = hasher.Hash("TestPass123!"),
                Name = "Force Off",
                Enabled = true,
                ForceTotp = true,
                MustChangePassword = false,
                LastPasswordChangeTime = DateTime.Now,
            });
        }

        using var scope2 = f.Services.CreateScope();
        var user = await scope2.ServiceProvider.GetRequiredService<IRepository<SysUser>>()
            .GetFirstAsync(u => u.Account.StartsWith("force_off_"));
        var policy = scope2.ServiceProvider.GetRequiredService<IMfaPolicyService>();
        Assert.False(await policy.IsTotpFeatureEnabledAsync());
        Assert.False(await policy.IsMfaRequiredAsync(user!));
    }

    [Fact]
    public async Task Enabling_at_runtime_enforces_force_totp_login()
    {
        await using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Totp:Enabled"] = "false",
                ["TenonAdmin:Security:DataProtection:Key"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            },
        };

        string account;
        const string password = "TestPass123!";
        using (var scope = f.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            account = "force_on_" + Guid.NewGuid().ToString("N")[..8];
            await users.InsertAsync(new SysUser
            {
                Account = account,
                Password = hasher.Hash(password),
                Name = "Force On",
                Enabled = true,
                ForceTotp = true,
                MustChangePassword = false,
                LastPasswordChangeTime = DateTime.Now,
            });
        }

        var admin = await SuperAdminClient(f);
        var batch = await admin.PutJson("/api/v1/sys/config/batch", new object[]
        {
            new { configKey = AdminTotpOptions.KEY_ENABLED, configValue = "true" },
        });
        Assert.Equal(0, (await batch.ReadEnvelope()).GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var policy = scope.ServiceProvider.GetRequiredService<IMfaPolicyService>();
            Assert.True(await policy.IsTotpFeatureEnabledAsync());
            var user = await scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>()
                .GetFirstAsync(u => u.Account == account);
            Assert.True(await policy.IsMfaRequiredAsync(user!));
        }

        var anon = f.CreateClient();
        var login = await (await anon.PostJson("/api/v1/auth/login", new { account, password })).ReadEnvelope();
        // 强制 MFA 且未绑定 → TotpNotBound
        Assert.Equal((int)ErrorCode.TotpNotBound, login.GetProperty("code").GetInt32());
    }
}
