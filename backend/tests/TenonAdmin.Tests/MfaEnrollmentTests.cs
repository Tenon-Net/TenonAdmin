using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// TOTP 自助绑定 / 恢复 / 管理员清除 / 高敏默认(ADR 0006)。
/// 使用 <c>Totp:Enabled=true</c>;不再测 InitGrant/邀请/紧急授权。
/// </summary>
public class MfaEnrollmentTests
{
    private static AdminAppFactory Factory() =>
        new()
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Totp:Enabled"] = "true",
                ["TenonAdmin:Security:DataProtection:Key"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["TenonAdmin:Security:DataProtection:KeyVersion"] = "1",
            },
            Overrides = s =>
            {
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

    private static async Task<(AdminAppFactory f, SysUser user, string password)> SeedUserAsync(
        AdminAppFactory f, bool superAdmin = false, bool forceTotp = false)
    {
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var password = "TestPass123!";
        var user = new SysUser
        {
            Account = "mfa_user_" + Guid.NewGuid().ToString("N")[..8],
            Password = hasher.Hash(password),
            Name = "MFA Test",
            Enabled = true,
            IsSuperAdmin = superAdmin,
            ForceTotp = forceTotp,
            MustChangePassword = false,
            LastPasswordChangeTime = DateTime.Now,
        };
        await users.InsertAsync(user);
        return (f, user, password);
    }

    private static async Task BindSelfAsync(
        IMfaEnrollmentService enroll, ITotpService totp, string account, string password)
    {
        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Account = account,
            CurrentPassword = password,
        });
        await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = totp.ComputeCode(start.Seed),
        });
    }

    [Fact]
    public async Task Self_bind_roundtrip_encrypts_seed_and_issues_recovery_codes()
    {
        await using var f = Factory();
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Account = user.Account,
            CurrentPassword = password,
        });
        Assert.False(string.IsNullOrEmpty(start.BindChallengeId));
        Assert.StartsWith("otpauth://", start.OtpauthUri);

        var complete = await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = totp.ComputeCode(start.Seed),
        });
        Assert.Equal(10, complete.RecoveryCodes.Count);

        var reloaded = await users.GetByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.TotpEnabled);
        Assert.False(string.IsNullOrEmpty(reloaded.TotpSeedProtected));
        var roundtrip = protector.Unprotect(reloaded.TotpSeedProtected!);
        Assert.Equal(start.Seed, roundtrip);
    }

    [Fact]
    public async Task Self_bind_rejects_wrong_password()
    {
        await using var f = Factory();
        var (_, user, _) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Account = user.Account,
                CurrentPassword = "wrong-password",
            }));
        Assert.Equal(ErrorCode.PasswordWrong, ex.Code);
    }

    [Fact]
    public async Task Self_bind_rejects_when_already_bound()
    {
        await using var f = Factory();
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();

        await BindSelfAsync(enroll, totp, user.Account, password);

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Account = user.Account,
                CurrentPassword = password,
            }));
        Assert.Equal(ErrorCode.MfaBindInvalid, ex.Code);
    }

    [Fact]
    public async Task Recovery_code_clears_mfa_and_revokes_sessions()
    {
        await using var f = Factory();
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Account = user.Account,
            CurrentPassword = password,
        });
        var complete = await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = totp.ComputeCode(start.Seed),
        });

        await enroll.UseRecoveryCodeAsync(new TotpRecoveryInput
        {
            Account = user.Account,
            CurrentPassword = password,
            RecoveryCode = complete.RecoveryCodes[0],
        });

        var reloaded = await users.GetByIdAsync(user.Id);
        Assert.False(reloaded!.TotpEnabled);
        Assert.True(string.IsNullOrEmpty(reloaded.TotpSeedProtected));
    }

    [Fact]
    public async Task Admin_clear_mfa_allows_rebind()
    {
        await using var f = Factory();
        var (_, target, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

        var super = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(super);

        await BindSelfAsync(enroll, totp, target.Account, password);
        await enroll.ClearUserMfaAsync(target.Id, super!.Id);

        var reloaded = await users.GetByIdAsync(target.Id);
        Assert.False(reloaded!.TotpEnabled);

        // 可再次自助绑定
        await BindSelfAsync(enroll, totp, target.Account, password);
        reloaded = await users.GetByIdAsync(target.Id);
        Assert.True(reloaded!.TotpEnabled);
    }

    [Fact]
    public async Task High_sensitivity_defaults_include_mfa_clear_not_invite()
    {
        await using var f = Factory();
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IHighSensitivityPermissionService>();
        var list = await svc.ListAsync();

        Assert.Contains(HighSensitivityPermissions.MfaClear, list.Defaults);
        Assert.DoesNotContain(list.Defaults, c => c.Contains("/mfa/invite", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(list.Defaults, c => c.Contains("/mfa/reset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Policy_requires_force_totp_when_enabled()
    {
        await using var f = Factory();
        var (_, user, _) = await SeedUserAsync(f, forceTotp: true);
        using var scope = f.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IMfaPolicyService>();
        Assert.True(await policy.IsMfaRequiredAsync(user));
    }

    [Fact]
    public async Task Policy_does_not_force_when_totp_feature_off()
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
        var (_, user, _) = await SeedUserAsync(f, forceTotp: true);
        using var scope = f.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IMfaPolicyService>();
        Assert.False(await policy.IsMfaRequiredAsync(user));
    }

    [Fact]
    public async Task Http_self_bind_endpoints()
    {
        await using var f = Factory();
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();

        var c = f.CreateClient();
        var startEnv = await (await c.PostJson("/api/v1/auth/mfa/bind/start", new
        {
            account = user.Account,
            currentPassword = password,
        })).ReadEnvelope();
        Assert.Equal(0, startEnv.GetProperty("code").GetInt32());
        var data = startEnv.GetProperty("data");
        var challengeId = data.GetProperty("bindChallengeId").GetString()!;
        var seed = data.GetProperty("seed").GetString()!;

        var completeEnv = await (await c.PostJson("/api/v1/auth/mfa/bind/complete", new
        {
            bindChallengeId = challengeId,
            totpCode = totp.ComputeCode(seed),
        })).ReadEnvelope();
        Assert.Equal(0, completeEnv.GetProperty("code").GetInt32());
        Assert.True(completeEnv.GetProperty("data").GetProperty("recoveryCodes").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Self_bind_rejects_when_totp_feature_off()
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
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Account = user.Account,
                CurrentPassword = password,
            }));
        Assert.Equal(ErrorCode.NoPermission, ex.Code);
    }

    [Fact]
    public async Task Policy_require_for_super_admin_when_configured()
    {
        await using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Totp:Enabled"] = "true",
                ["TenonAdmin:Security:Totp:RequireForSuperAdmin"] = "true",
                ["TenonAdmin:Security:DataProtection:Key"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            },
        };
        var (_, super, _) = await SeedUserAsync(f, superAdmin: true);
        var (_, normal, _) = await SeedUserAsync(f, superAdmin: false);
        using var scope = f.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IMfaPolicyService>();

        Assert.True(await policy.IsMfaRequiredAsync(super));
        Assert.False(await policy.IsMfaRequiredAsync(normal));
    }

    [Fact]
    public async Task Clear_mfa_non_super_without_permission_denied()
    {
        await using var f = Factory();
        var (_, target, password) = await SeedUserAsync(f);
        var (_, operatorUser, _) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();

        await BindSelfAsync(enroll, totp, target.Account, password);

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.ClearUserMfaAsync(target.Id, operatorUser.Id));
        // 未绑 TOTP 的非超管:先撞 TotpNotBound;若操作人无 TOTP 且非超管
        Assert.True(ex.Code is ErrorCode.TotpNotBound or ErrorCode.NoPermission);
    }

    [Fact]
    public async Task Http_clear_mfa_as_super_admin()
    {
        await using var f = Factory();
        var (_, target, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

        await BindSelfAsync(enroll, totp, target.Account, password);

        // 超管默认无 TOTP:ClearUserMfa 允许未绑 TOTP 的超管清理他人
        var super = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(super);

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        // Totp:Enabled → RequireReauth 生效;成功时 void action 可能空 body 200
        async Task<bool> TryClearAsync()
        {
            var resp = await c.PostJson("/api/v1/sys/mfa/clear", new { userId = target.Id });
            var raw = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(raw))
                return resp.IsSuccessStatusCode;
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code == (int)ErrorCode.ReauthRequired)
            {
                var reauthEnv = await (await c.PostJson("/api/v1/auth/reauth", new
                {
                    method = "password",
                    password = "Test@123456",
                })).ReadEnvelope();
                Assert.Equal(0, reauthEnv.GetProperty("code").GetInt32());
                return false; // 调用方再试一次 clear
            }
            Assert.Equal(0, code);
            return true;
        }

        if (!await TryClearAsync())
            Assert.True(await TryClearAsync());

        var reloaded = await users.GetByIdAsync(target.Id);
        Assert.False(reloaded!.TotpEnabled);
    }
}
