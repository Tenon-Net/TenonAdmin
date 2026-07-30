using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// MFA 绑定/恢复/高敏默认/seed 加密:驱动真实 enrollment + protector + session 服务。
/// 强制 MFA 依赖 <see cref="ISecurityProfileAccessor.IsLevel3"/>——测试用桩返回 true,
/// 不把部署 Profile 设为 Level3(避免 Redis TLS 启动预检阻断集成测试宿主)。
/// </summary>
public class MfaEnrollmentTests
{
    /// <summary>测试用 Level3 档位桩:IsLevel3=true,不触发启动预检(预检读 Options.Profile)。</summary>
    private sealed class StubLevel3Profile : ISecurityProfileAccessor
    {
        public SecurityProfile Profile => SecurityProfile.Level3;
        public bool IsLevel3 => true;
        public bool IsProductionWithoutLevel3 => false;
    }

    private static AdminAppFactory Factory(bool forceMfaProfile = true) =>
        new()
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Level3:InitGrant"] = "test-init-grant-super-secret-token-32b",
                ["TenonAdmin:Security:Level3:InitGrantNotAfter"] = DateTimeOffset.UtcNow.AddHours(2).ToString("O"),
                ["TenonAdmin:Security:Level3:EmergencyGrant"] = "test-emergency-grant-super-secret-32b",
                ["TenonAdmin:Security:Level3:EmergencyGrantNotAfter"] = DateTimeOffset.UtcNow.AddHours(2).ToString("O"),
                ["TenonAdmin:Security:DataProtection:Key"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["TenonAdmin:Security:DataProtection:KeyVersion"] = "1",
            },
            Overrides = forceMfaProfile
                ? s =>
                {
                    // 策略层看 IsLevel3=true,但去掉启动 Redis 预检闸门(测试宿主无 TLS Redis)
                    s.Replace(ServiceDescriptor.Singleton<ISecurityProfileAccessor, StubLevel3Profile>());
                    foreach (var d in s.ToList())
                    {
                        if (d.ServiceType != typeof(IHostedService)) continue;
                        var name = d.ImplementationType?.Name
                                   ?? d.ImplementationInstance?.GetType().Name
                                   ?? "";
                        if (name.Contains("Level3Startup", StringComparison.Ordinal))
                            s.Remove(d);
                    }
                }
                : null,
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

    /// <summary>Level3 下发邀请要求发起人已绑 TOTP——把种子超管标为已绑定以便发邀请。</summary>
    private static async Task<long> MarkSuperAdminTotpReadyAsync(IServiceProvider sp)
    {
        var (_, id) = await MarkSuperAdminTotpReadyWithSeedAsync(sp);
        return id;
    }

    /// <summary>同上,并返回明文 seed(HTTP reauth / 登录完成流用)。</summary>
    private static async Task<(string seed, long userId)> MarkSuperAdminTotpReadyWithSeedAsync(IServiceProvider sp)
    {
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var protector = sp.GetRequiredService<ISecretProtector>();
        var totp = sp.GetRequiredService<ITotpService>();
        var super = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(super);
        var seed = totp.GenerateSeed();
        super!.TotpEnabled = true;
        super.TotpSeedProtected = protector.Protect(seed);
        super.TotpBoundAt = DateTime.Now;
        await users.UpdateAsync(super);
        return (seed, super.Id);
    }

    /// <summary>Level3 下密码登录触发 40018 后,用 TOTP 完成 HTTP 登录并挂 Bearer。</summary>
    private static async Task<HttpClient> LoginSuperAdminWithTotpAsync(
        AdminAppFactory f, string seed, string password = "Test@123456")
    {
        using var scope = f.Services.CreateScope();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var c = f.CreateClient();
        var loginResp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password });
        var loginEnv = await loginResp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpRequired, loginEnv.GetProperty("code").GetInt32());
        var challengeId = loginEnv.GetProperty("args").GetProperty("challengeId").GetString();
        Assert.False(string.IsNullOrEmpty(challengeId));

        var totpResp = await c.PostJson("/api/v1/auth/login/totp", new
        {
            challengeId,
            code = totp.ComputeCode(seed),
        });
        var totpEnv = await totpResp.ReadEnvelope();
        Assert.Equal(0, totpEnv.GetProperty("code").GetInt32());
        var access = totpEnv.GetProperty("data").GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrEmpty(access));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return c;
    }

    private static async Task ReauthWithTotpAsync(HttpClient c, ITotpService totp, string seed)
    {
        var resp = await c.PostJson("/api/v1/auth/reauth", new
        {
            method = "totp",
            totpCode = totp.ComputeCode(seed),
        });
        var env = await resp.ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public void High_sensitivity_defaults_are_immutable_frozen_set()
    {
        Assert.True(HighSensitivityPermissions.Default.Count >= 10);
        Assert.True(HighSensitivityPermissions.IsDefault("POST:/api/v1/sys/user"));
        Assert.True(HighSensitivityPermissions.IsDefault("PUT:/api/v1/sys/role/menu"));
        Assert.True(HighSensitivityPermissions.IsDefault("DELETE:/api/v1/sys/session/{sessionid}"));
        Assert.True(HighSensitivityPermissions.IsDefault("POST:/api/v1/sys/mfa/invite"));
        Assert.True(HighSensitivityPermissions.IsDefault("DELETE:/api/v1/sys/mfa/invite/{id:long}"));
        Assert.True(HighSensitivityPermissions.IsDefault("DELETE:/api/v1/sys/mfa/high-sensitivity/{id:long}"));
        Assert.False(HighSensitivityPermissions.IsDefault("DELETE:/api/v1/sys/mfa/invite/{id}"));
        Assert.False(HighSensitivityPermissions.IsDefault("GET:/api/v1/ping"));

        Assert.ThrowsAny<Exception>(() =>
        {
            var mutable = (ISet<string>)HighSensitivityPermissions.Default;
            mutable.Add("hack");
        });
    }

    [Fact]
    public async Task Bind_without_password_fails()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, user, _) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();

        var invite = await enroll.IssueBindInviteAsync(user.Id, issuedByUserId: 1);
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Token = invite.Token,
                CurrentPassword = "",
            }));
        Assert.Equal(ErrorCode.MfaBindPasswordRequired, ex.Code);
    }

    [Fact]
    public async Task Bind_with_wrong_password_fails()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, user, _) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();

        var invite = await enroll.IssueBindInviteAsync(user.Id, issuedByUserId: 1);
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Token = invite.Token,
                CurrentPassword = "wrong-password",
            }));
        Assert.Equal(ErrorCode.PasswordWrong, ex.Code);
    }

    [Fact]
    public async Task Successful_bind_stores_only_protected_seed_and_hashed_recovery_codes()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var codes = scope.ServiceProvider.GetRequiredService<IRepository<SysTotpRecoveryCode>>();

        var invite = await enroll.IssueBindInviteAsync(user.Id, issuedByUserId: 1);
        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Token = invite.Token,
            CurrentPassword = password,
        });
        Assert.False(string.IsNullOrEmpty(start.Seed));
        Assert.Contains("otpauth://totp/", start.OtpauthUri);

        var code = totp.ComputeCode(start.Seed);
        var done = await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = code,
        });
        Assert.Equal(10, done.RecoveryCodes.Count);

        var reloaded = await users.GetByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.TotpEnabled);
        Assert.False(string.IsNullOrEmpty(reloaded.TotpSeedProtected));
        Assert.DoesNotContain(start.Seed, reloaded.TotpSeedProtected);
        Assert.Equal(start.Seed, protector.Unprotect(reloaded.TotpSeedProtected!));

        var stored = await codes.AsQueryable().Where(c => c.UserId == user.Id).ToListAsync();
        Assert.Equal(10, stored.Count);
        foreach (var c in stored)
        {
            Assert.False(string.IsNullOrEmpty(c.CodeHash));
            foreach (var plain in done.RecoveryCodes)
                Assert.DoesNotContain(plain, c.CodeHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Wrong_totp_code_does_not_consume_bind_challenge()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();

        var invite = await enroll.IssueBindInviteAsync(user.Id, issuedByUserId: 1);
        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Token = invite.Token,
            CurrentPassword = password,
        });
        var correctCode = totp.ComputeCode(start.Seed);
        var wrongCode = correctCode == "000000" ? "000001" : "000000";

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.CompleteBindAsync(new TotpBindCompleteInput
            {
                BindChallengeId = start.BindChallengeId,
                TotpCode = wrongCode,
            }));
        Assert.Equal(ErrorCode.TotpWrong, ex.Code);

        var completed = await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = correctCode,
        });
        Assert.Equal(10, completed.RecoveryCodes.Count);
    }

    [Fact]
    public async Task Recovery_code_clears_totp_and_revokes_sessions()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, user, password) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenProvider>();
        var policy = scope.ServiceProvider.GetRequiredService<ISecurityPolicyProvider>();

        var invite = await enroll.IssueBindInviteAsync(user.Id, 1);
        var start = await enroll.StartBindAsync(new TotpBindStartInput { Token = invite.Token, CurrentPassword = password });
        var done = await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = totp.ComputeCode(start.Seed),
        });

        var reloaded = (await users.GetByIdAsync(user.Id))!;
        var (accessMin, refreshMin) = await policy.GetSessionTtlAsync();
        var sid = Guid.CreateVersion7().ToString("N");
        var pair = tokens.Create(
            new TokenSubject(reloaded.Id, reloaded.Account, sid, reloaded.IsSuperAdmin, reloaded.OrgId),
            TimeSpan.FromMinutes(accessMin), TimeSpan.FromMinutes(refreshMin));
        await sessions.OpenAsync(reloaded, sid, pair);
        Assert.True(await sessions.IsActiveAsync(sid));

        await enroll.UseRecoveryCodeAsync(new TotpRecoveryInput
        {
            Account = reloaded.Account,
            CurrentPassword = password,
            RecoveryCode = done.RecoveryCodes[0],
        });

        var after = await users.GetByIdAsync(user.Id);
        Assert.False(after!.TotpEnabled);
        Assert.True(string.IsNullOrEmpty(after.TotpSeedProtected));
        Assert.False(await sessions.IsActiveAsync(sid));

        var replay = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.UseRecoveryCodeAsync(new TotpRecoveryInput
            {
                Account = reloaded.Account,
                CurrentPassword = password,
                RecoveryCode = done.RecoveryCodes[0],
            }));
        Assert.Equal(ErrorCode.RecoveryCodeInvalid, replay.Code);
    }

    [Fact]
    public async Task Super_admin_is_mfa_required_and_force_totp_user_too()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, super, _) = await SeedUserAsync(f, superAdmin: true);
        var (_, forced, _) = await SeedUserAsync(f, forceTotp: true);
        var (_, normal, _) = await SeedUserAsync(f);

        using var scope = f.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IMfaPolicyService>();

        Assert.True(await policy.IsMfaRequiredAsync(super));
        Assert.True(await policy.IsMfaRequiredAsync(forced));
        Assert.False(await policy.IsMfaRequiredAsync(normal));
    }

    [Fact]
    public async Task Non_level3_does_not_force_mfa()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, super, _) = await SeedUserAsync(f, superAdmin: true);
        using var scope = f.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IMfaPolicyService>();
        Assert.False(await policy.IsMfaRequiredAsync(super));
    }

    [Fact]
    public async Task Init_grant_bind_for_super_admin()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, super, password) = await SeedUserAsync(f, superAdmin: true);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();

        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Token = security.Level3.InitGrant!,
            CurrentPassword = password,
            Account = super.Account,
        });
        await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = totp.ComputeCode(start.Seed),
        });

        var after = await users.GetByIdAsync(super.Id);
        Assert.True(after!.TotpEnabled);

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Token = security.Level3.InitGrant!,
                CurrentPassword = password,
                Account = super.Account,
            }));
        Assert.Equal(ErrorCode.BindInviteInvalid, ex.Code);
    }

    [Fact]
    public async Task Reauth_grant_and_check_window()
    {
        using var f = Factory(forceMfaProfile: false);
        using var scope = f.Services.CreateScope();
        var reauth = scope.ServiceProvider.GetRequiredService<IReauthService>();
        const long uid = 42;
        Assert.False(await reauth.IsGrantedAsync(uid));
        await reauth.GrantAsync(uid, "totp");
        Assert.True(await reauth.IsGrantedAsync(uid));
        await reauth.RevokeAsync(uid);
        Assert.False(await reauth.IsGrantedAsync(uid));
    }

    [Fact]
    public async Task Level3_login_rejects_unbound_mfa_required_user()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, user, password) = await SeedUserAsync(f, forceTotp: true);
        using var scope = f.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            auth.LoginAsync(new LoginInput { Account = user.Account, Password = password }));
        Assert.Equal(ErrorCode.TotpNotBound, ex.Code);
    }

    [Fact]
    public async Task Level3_phone_login_cannot_bypass_force_totp()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, user, _) = await SeedUserAsync(f, forceTotp: true);
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();
        var configs = scope.ServiceProvider.GetRequiredService<IConfigService>();

        const string phone = "13800138000";
        user.Phone = phone;
        await users.UpdateAsync(user);

        await configs.SaveValuesAsync([
            new ConfigBatchItem { ConfigKey = "sys.security.smsLogin.enabled", ConfigValue = "true" },
        ]);

        // 手工写入与 SmsOtpService 相同键的 OTP,使 Verify 通过后必达 TOTP 门禁
        const string code = "654321";
        await cache.SetAsync(CacheKeys.SmsCode(ISmsOtpService.PURPOSE_LOGIN, phone), code, TimeSpan.FromMinutes(5));

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            auth.LoginByPhoneAsync(new PhoneLoginInput { Phone = phone, Code = code }));
        Assert.Equal(ErrorCode.TotpNotBound, ex.Code);
    }

    [Fact]
    public async Task Level3_invite_requires_issuer_totp_enabled()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, user, _) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

        var super = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(super);
        super!.TotpEnabled = false;
        await users.UpdateAsync(super);

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.IssueBindInviteAsync(user.Id, super.Id));
        Assert.Equal(ErrorCode.TotpNotBound, ex.Code);

        await MarkSuperAdminTotpReadyAsync(scope.ServiceProvider);
        var invite = await enroll.IssueBindInviteAsync(user.Id, super.Id);
        Assert.False(string.IsNullOrEmpty(invite.Token));
    }

    [Fact]
    public async Task Level3_invite_rejects_system_issuer_zero()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, user, _) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.IssueBindInviteAsync(user.Id, issuedByUserId: 0));
        Assert.Equal(ErrorCode.NoPermission, ex.Code);
    }

    [Fact]
    public async Task Level3_revoke_invite_requires_operator_totp()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, user, _) = await SeedUserAsync(f);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

        var issuerId = await MarkSuperAdminTotpReadyAsync(scope.ServiceProvider);
        var invite = await enroll.IssueBindInviteAsync(user.Id, issuerId);
        // 建一条可撤销的邀请实体:Issue 已插入;用 issuer 撤
        var invites = scope.ServiceProvider.GetRequiredService<IRepository<SysTotpBindInvite>>();
        var row = await invites.GetFirstAsync(i => i.UserId == user.Id && i.ConsumedAt == null);
        Assert.NotNull(row);

        // 无 TOTP 的操作者
        var bare = await SeedUserAsync(f);
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.RevokeBindInviteAsync(row!.Id, bare.user.Id));
        Assert.Equal(ErrorCode.TotpNotBound, ex.Code);

        // 已绑 TOTP 的超管可撤
        await enroll.RevokeBindInviteAsync(row.Id, issuerId);
        var after = await invites.GetByIdAsync(row.Id);
        Assert.NotNull(after!.RevokedAt);
        _ = users;
        _ = invite;
    }

    [Fact]
    public async Task User_service_roundtrips_force_totp_flag()
    {
        using var f = Factory(forceMfaProfile: false);
        using var scope = f.Services.CreateScope();
        var userSvc = scope.ServiceProvider.GetRequiredService<IUserService>();
        var account = "force_totp_" + Guid.NewGuid().ToString("N")[..8];

        var created = await userSvc.AddAsync(new AddUserInput
        {
            Account = account,
            Name = "Force Totp User",
            Enabled = true,
            ForceTotp = true,
            RoleIds = [],
        });
        var detail = await userSvc.GetAsync(created.Id);
        Assert.True(detail.ForceTotp);
        Assert.False(detail.TotpEnabled);

        await userSvc.UpdateAsync(created.Id, new UpdateUserInput
        {
            Name = detail.Name,
            Nickname = detail.Nickname,
            Phone = detail.Phone,
            Email = detail.Email,
            Gender = detail.Gender,
            Avatar = detail.Avatar,
            OrgId = detail.OrgId,
            PositionId = detail.PositionId,
            DirectorId = detail.DirectorId,
            Enabled = detail.Enabled,
            ForceTotp = false,
            RoleIds = detail.RoleIds,
        });
        detail = await userSvc.GetAsync(created.Id);
        Assert.False(detail.ForceTotp);
    }

    [Fact]
    public async Task High_sensitivity_custom_add_delete_and_default_immutable()
    {
        using var f = Factory(forceMfaProfile: true);
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IHighSensitivityPermissionService>();
        var issuerId = await MarkSuperAdminTotpReadyAsync(scope.ServiceProvider);

        var list = await svc.ListAsync();
        Assert.NotEmpty(list.Defaults);
        Assert.Contains(HighSensitivityPermissions.MfaInvite, list.Defaults);

        // 不可追加默认集中的码
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            svc.AddAsync(new HighSensitivityPermissionInput { PermissionCode = HighSensitivityPermissions.MfaInvite }, issuerId));
        Assert.Equal(ErrorCode.NoPermission, ex.Code);

        var row = await svc.AddAsync(new HighSensitivityPermissionInput
        {
            PermissionCode = "POST:/api/v1/biz/demo",
            Remark = "test",
        }, issuerId);
        Assert.True(row.Id > 0);

        list = await svc.ListAsync();
        Assert.Contains(list.Customs, c => c.PermissionCode == "POST:/api/v1/biz/demo");

        await svc.DeleteAsync(row.Id, issuerId);
        list = await svc.ListAsync();
        Assert.DoesNotContain(list.Customs, c => c.PermissionCode == "POST:/api/v1/biz/demo");
    }

    [Fact]
    public async Task Level3_login_totp_challenge_roundtrip()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, user, password) = await SeedUserAsync(f, forceTotp: true);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var issuerId = await MarkSuperAdminTotpReadyAsync(scope.ServiceProvider);
        var invite = await enroll.IssueBindInviteAsync(user.Id, issuerId);
        var start = await enroll.StartBindAsync(new TotpBindStartInput { Token = invite.Token, CurrentPassword = password });
        await enroll.CompleteBindAsync(new TotpBindCompleteInput
        {
            BindChallengeId = start.BindChallengeId,
            TotpCode = totp.ComputeCode(start.Seed),
        });

        var challengeEx = await Assert.ThrowsAsync<AdminException>(() =>
            auth.LoginAsync(new LoginInput { Account = user.Account, Password = password }));
        Assert.Equal(ErrorCode.TotpRequired, challengeEx.Code);
        Assert.NotNull(challengeEx.Args);
        var challengeId = challengeEx.Args!["challengeId"]?.ToString();
        Assert.False(string.IsNullOrEmpty(challengeId));

        var login = await auth.LoginByTotpChallengeAsync(new TotpChallengeLoginInput
        {
            ChallengeId = challengeId!,
            Code = totp.ComputeCode(start.Seed),
        });
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.Equal(user.Account, login.Account);
    }

    /// <summary>另一名已绑 TOTP 的超管可批准重置目标超管 MFA,并下发重绑邀请。</summary>
    [Fact]
    public async Task ResetSuperAdmin_peer_approval_clears_mfa_and_issues_invite()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, peer, peerPwd) = await SeedUserAsync(f, superAdmin: true);
        var (_, target, targetPwd) = await SeedUserAsync(f, superAdmin: true);

        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();

        // 双方均完成 TOTP 绑定(peer 用邀请, target 用另一邀请)
        async Task BindAsync(SysUser u, string password)
        {
            var invite = await enroll.IssueBindInviteAsync(u.Id, 1);
            var start = await enroll.StartBindAsync(new TotpBindStartInput
            {
                Token = invite.Token,
                CurrentPassword = password,
            });
            await enroll.CompleteBindAsync(new TotpBindCompleteInput
            {
                BindChallengeId = start.BindChallengeId,
                TotpCode = totp.ComputeCode(start.Seed),
            });
        }

        await BindAsync(peer, peerPwd);
        await BindAsync(target, targetPwd);
        Assert.True((await users.GetByIdAsync(target.Id))!.TotpEnabled);

        var reset = await enroll.ResetSuperAdminMfaAsync(
            new TotpSuperAdminResetInput { TargetUserId = target.Id, Mode = "peer" },
            operatorUserId: peer.Id);

        Assert.False(string.IsNullOrEmpty(reset.Invite.Token));
        var after = await users.GetByIdAsync(target.Id);
        Assert.False(after!.TotpEnabled);
        Assert.True(string.IsNullOrEmpty(after.TotpSeedProtected));

        // peer 不能重置自己
        var self = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.ResetSuperAdminMfaAsync(
                new TotpSuperAdminResetInput { TargetUserId = peer.Id, Mode = "peer" },
                operatorUserId: peer.Id));
        Assert.Equal(ErrorCode.NoPermission, self.Code);
        _ = security; // keep options wired for Level3 grant settings in other cases
    }

    /// <summary>唯一超管可用部署紧急授权重置 MFA;多于一名超管时拒绝紧急路径。</summary>
    [Fact]
    public async Task ResetSuperAdmin_emergency_sole_ok_multi_reject()
    {
        using var f = Factory(forceMfaProfile: false);
        var (_, sole, password) = await SeedUserAsync(f, superAdmin: true);

        using (var scope = f.Services.CreateScope())
        {
            var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
            var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
            var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();

            // 关掉种子超管,保证系统中仅有一名 IsSuperAdmin(sole)
            var seeded = await users.AsQueryable().Where(u => u.IsSuperAdmin && u.Id != sole.Id).ToListAsync();
            foreach (var s in seeded)
            {
                s.IsSuperAdmin = false;
                await users.UpdateAsync(s);
            }

            var invite = await enroll.IssueBindInviteAsync(sole.Id, 1);
            var start = await enroll.StartBindAsync(new TotpBindStartInput
            {
                Token = invite.Token,
                CurrentPassword = password,
            });
            await enroll.CompleteBindAsync(new TotpBindCompleteInput
            {
                BindChallengeId = start.BindChallengeId,
                TotpCode = totp.ComputeCode(start.Seed),
            });
            Assert.True((await users.GetByIdAsync(sole.Id))!.TotpEnabled);

            var reset = await enroll.ResetSuperAdminMfaAsync(
                new TotpSuperAdminResetInput
                {
                    TargetUserId = sole.Id,
                    Mode = "emergency",
                    EmergencyGrant = security.Level3.EmergencyGrant,
                },
                operatorUserId: null);
            Assert.False(string.IsNullOrEmpty(reset.Invite.Token));
            Assert.False((await users.GetByIdAsync(sole.Id))!.TotpEnabled);

            // 紧急授权一次性消费
            var replay = await Assert.ThrowsAsync<AdminException>(() =>
                enroll.ResetSuperAdminMfaAsync(
                    new TotpSuperAdminResetInput
                    {
                        TargetUserId = sole.Id,
                        Mode = "emergency",
                        EmergencyGrant = security.Level3.EmergencyGrant,
                    },
                    operatorUserId: null));
            Assert.Equal(ErrorCode.BindInviteInvalid, replay.Code);
        }

        // 多超管:紧急路径拒绝
        using var f2 = Factory(forceMfaProfile: false);
        var (_, a, aPwd) = await SeedUserAsync(f2, superAdmin: true);
        var (_, b, bPwd) = await SeedUserAsync(f2, superAdmin: true);
        using (var scope2 = f2.Services.CreateScope())
        {
            var enroll = scope2.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
            var totp = scope2.ServiceProvider.GetRequiredService<ITotpService>();
            var security = scope2.ServiceProvider.GetRequiredService<AdminSecurityOptions>();
            // 至少绑定目标,便于路径走到 superCount 检查
            var inv = await enroll.IssueBindInviteAsync(a.Id, 1);
            var st = await enroll.StartBindAsync(new TotpBindStartInput { Token = inv.Token, CurrentPassword = aPwd });
            await enroll.CompleteBindAsync(new TotpBindCompleteInput
            {
                BindChallengeId = st.BindChallengeId,
                TotpCode = totp.ComputeCode(st.Seed),
            });
            _ = b;
            _ = bPwd;

            var multi = await Assert.ThrowsAsync<AdminException>(() =>
                enroll.ResetSuperAdminMfaAsync(
                    new TotpSuperAdminResetInput
                    {
                        TargetUserId = a.Id,
                        Mode = "emergency",
                        EmergencyGrant = security.Level3.EmergencyGrant,
                    },
                    operatorUserId: null));
            // 多超管 → NoPermission;若 emergency 已在本厂未被消费则走 superCount
            Assert.Equal(ErrorCode.NoPermission, multi.Code);
        }
    }

    /// <summary>
    /// HTTP:Level3 下用户新增/更新 ForceTotp 须再认证;未授予 → 40024,授予后可写。
    /// </summary>
    [Fact]
    public async Task Http_force_totp_user_write_requires_reauth()
    {
        using var f = Factory(forceMfaProfile: true);
        string seed;
        using (var scope = f.Services.CreateScope())
        {
            (seed, _) = await MarkSuperAdminTotpReadyWithSeedAsync(scope.ServiceProvider);
        }

        using var admin = await LoginSuperAdminWithTotpAsync(f, seed);
        using var totpScope = f.Services.CreateScope();
        var totp = totpScope.ServiceProvider.GetRequiredService<ITotpService>();

        var account = "http_force_" + Guid.NewGuid().ToString("N")[..8];
        var addBody = new
        {
            account,
            name = "Force Totp Http",
            password = "TestPass123!",
            enabled = true,
            forceTotp = true,
            roleIds = Array.Empty<long>(),
        };

        // 无 reauth → 拒绝
        var denied = await (await admin.PostJson("/api/v1/sys/user", addBody)).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ReauthRequired, denied.GetProperty("code").GetInt32());

        await ReauthWithTotpAsync(admin, totp, seed);

        var added = await (await admin.PostJson("/api/v1/sys/user", addBody)).ReadEnvelope();
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var userId = added.GetProperty("data").GetProperty("id").GetInt64();

        // reauth 窗口内可更新 ForceTotp;先读详情拼完整 PUT 体
        var detail = await (await admin.GetAsync($"/api/v1/sys/user/{userId}")).ReadEnvelope();
        Assert.True(detail.GetProperty("data").GetProperty("forceTotp").GetBoolean());

        var d = detail.GetProperty("data");
        var updateBody = new
        {
            name = d.GetProperty("name").GetString(),
            nickname = d.TryGetProperty("nickname", out var nick) ? nick.GetString() : null,
            phone = d.TryGetProperty("phone", out var ph) ? ph.GetString() : null,
            email = d.TryGetProperty("email", out var em) ? em.GetString() : null,
            gender = d.TryGetProperty("gender", out var g) && g.ValueKind != JsonValueKind.Null ? g.GetInt32() : (int?)null,
            avatar = d.TryGetProperty("avatar", out var av) ? av.GetString() : null,
            orgId = d.TryGetProperty("orgId", out var org) && org.ValueKind != JsonValueKind.Null ? org.GetInt64() : (long?)null,
            positionId = d.TryGetProperty("positionId", out var pos) && pos.ValueKind != JsonValueKind.Null ? pos.GetInt64() : (long?)null,
            directorId = d.TryGetProperty("directorId", out var dir) && dir.ValueKind != JsonValueKind.Null ? dir.GetInt64() : (long?)null,
            enabled = d.GetProperty("enabled").GetBoolean(),
            forceTotp = false,
            roleIds = d.TryGetProperty("roleIds", out var roles)
                ? roles.EnumerateArray().Select(x => x.GetInt64()).ToArray()
                : Array.Empty<long>(),
        };

        // 吊销 reauth 后再写 → 再次拒绝
        using (var scope = f.Services.CreateScope())
        {
            var reauth = scope.ServiceProvider.GetRequiredService<IReauthService>();
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
            var super = await users.GetFirstAsync(u => u.Account == "superAdmin");
            await reauth.RevokeAllForUserAsync(super!.Id);
        }

        var deniedUpdate = await (await admin.PutJson($"/api/v1/sys/user/{userId}", updateBody)).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ReauthRequired, deniedUpdate.GetProperty("code").GetInt32());

        await ReauthWithTotpAsync(admin, totp, seed);
        var updated = await (await admin.PutJson($"/api/v1/sys/user/{userId}", updateBody)).ReadEnvelope();
        Assert.Equal(0, updated.GetProperty("code").GetInt32());

        var after = await (await admin.GetAsync($"/api/v1/sys/user/{userId}")).ReadEnvelope();
        Assert.False(after.GetProperty("data").GetProperty("forceTotp").GetBoolean());
    }

    /// <summary>
    /// HTTP 闭环:ForceTotp 用户 → 再认证签发邀请 → 密码确认绑定 → TOTP 登录;
    /// 邀请仅一次可用。
    /// </summary>
    [Fact]
    public async Task Http_force_totp_invite_bind_and_totp_login_roundtrip()
    {
        using var f = Factory(forceMfaProfile: true);
        string seed;
        using (var scope = f.Services.CreateScope())
        {
            (seed, _) = await MarkSuperAdminTotpReadyWithSeedAsync(scope.ServiceProvider);
        }

        // 先建 ForceTotp 目标用户(服务层,避免本测与 reauth 用例耦合失败点)
        var (_, target, password) = await SeedUserAsync(f, forceTotp: true);

        using var admin = await LoginSuperAdminWithTotpAsync(f, seed);
        using var totpScope = f.Services.CreateScope();
        var totp = totpScope.ServiceProvider.GetRequiredService<ITotpService>();

        // 无 reauth 不能签发邀请
        var inviteDenied = await (await admin.PostJson("/api/v1/sys/mfa/invite", new { userId = target.Id })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ReauthRequired, inviteDenied.GetProperty("code").GetInt32());

        await ReauthWithTotpAsync(admin, totp, seed);
        var inviteEnv = await (await admin.PostJson("/api/v1/sys/mfa/invite", new { userId = target.Id })).ReadEnvelope();
        Assert.Equal(0, inviteEnv.GetProperty("code").GetInt32());
        var inviteToken = inviteEnv.GetProperty("data").GetProperty("token").GetString();
        Assert.False(string.IsNullOrEmpty(inviteToken));

        // 匿名绑定:密码确认 → 完成 TOTP
        var anon = f.CreateClient();
        var startEnv = await (await anon.PostJson("/api/v1/auth/mfa/bind/start", new
        {
            token = inviteToken,
            currentPassword = password,
        })).ReadEnvelope();
        Assert.Equal(0, startEnv.GetProperty("code").GetInt32());
        var bindChallengeId = startEnv.GetProperty("data").GetProperty("bindChallengeId").GetString();
        var userSeed = startEnv.GetProperty("data").GetProperty("seed").GetString();
        Assert.False(string.IsNullOrEmpty(bindChallengeId));
        Assert.False(string.IsNullOrEmpty(userSeed));

        var completeEnv = await (await anon.PostJson("/api/v1/auth/mfa/bind/complete", new
        {
            bindChallengeId,
            totpCode = totp.ComputeCode(userSeed!),
        })).ReadEnvelope();
        Assert.Equal(0, completeEnv.GetProperty("code").GetInt32());
        var recovery = completeEnv.GetProperty("data").GetProperty("recoveryCodes");
        Assert.Equal(JsonValueKind.Array, recovery.ValueKind);
        Assert.True(recovery.GetArrayLength() >= 1);

        // 邀请仅一次:再次 start 应失败
        var reuse = await (await anon.PostJson("/api/v1/auth/mfa/bind/start", new
        {
            token = inviteToken,
            currentPassword = password,
        })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.BindInviteInvalid, reuse.GetProperty("code").GetInt32());

        // 未绑时已被拒(前置覆盖);绑定后密码登录 → 40018 → TOTP 完成
        var login1 = await (await anon.PostJson("/api/v1/auth/login", new
        {
            account = target.Account,
            password,
        })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpRequired, login1.GetProperty("code").GetInt32());
        var challengeId = login1.GetProperty("args").GetProperty("challengeId").GetString();
        Assert.False(string.IsNullOrEmpty(challengeId));

        var login2 = await (await anon.PostJson("/api/v1/auth/login/totp", new
        {
            challengeId,
            code = totp.ComputeCode(userSeed!),
        })).ReadEnvelope();
        Assert.Equal(0, login2.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(login2.GetProperty("data").GetProperty("accessToken").GetString()));
        Assert.Equal(target.Account, login2.GetProperty("data").GetProperty("account").GetString());
    }

    /// <summary>已有绑定超管后,另一超管不得用 InitGrant 绑定(必须走邀请)。</summary>
    [Fact]
    public async Task InitGrant_rejected_when_any_superadmin_already_bound()
    {
        using var f = Factory(forceMfaProfile: true);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();
        await MarkSuperAdminTotpReadyWithSeedAsync(scope.ServiceProvider);

        var (_, other, password) = await SeedUserAsync(f, superAdmin: true);
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Token = security.Level3.InitGrant!,
                CurrentPassword = password,
                Account = other.Account,
            }));
        Assert.Equal(ErrorCode.BindInviteInvalid, ex.Code);
    }

    /// <summary>并发消费 InitGrant:仅一方成功(条件更新 affected=1)。</summary>
    [Fact]
    public async Task Deploy_grant_consume_is_atomic_under_concurrency()
    {
        using var f = Factory(forceMfaProfile: true);
        using var scope = f.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ILevel3DeployGrantStore>();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();
        // 使用独立 hash 避免污染其它 InitGrant 用例
        var grantHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var notAfter = security.Level3.InitGrantNotAfter;
        await store.EnsureWithinTtlAsync(Level3DeployGrantKinds.Init, grantHash, 60, notAfter);

        var results = await Task.WhenAll(
            Task.Run(async () =>
            {
                try
                {
                    await store.ConsumeAsync(Level3DeployGrantKinds.Init, grantHash, 60, notAfter);
                    return true;
                }
                catch (AdminException) { return false; }
            }),
            Task.Run(async () =>
            {
                try
                {
                    await store.ConsumeAsync(Level3DeployGrantKinds.Init, grantHash, 60, notAfter);
                    return true;
                }
                catch (AdminException) { return false; }
            }));

        Assert.Equal(1, results.Count(x => x));
        var usable = await store.CheckUsableAsync(Level3DeployGrantKinds.Init, grantHash, 60, notAfter);
        Assert.False(usable.Usable);
    }

    /// <summary>匿名 emergency-reset HTTP:唯一超管无会话可达;错误 grant / 重放拒绝。</summary>
    [Fact]
    public async Task Http_emergency_reset_anonymous_sole_superadmin_roundtrip()
    {
        using var f = Factory(forceMfaProfile: true);
        string emergency;
        using (var scope = f.Services.CreateScope())
        {
            emergency = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>().Level3.EmergencyGrant!;
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
            // 仅保留一名超管并完成 TOTP
            var supers = await users.AsQueryable().Where(u => u.IsSuperAdmin).ToListAsync();
            SysUser? sole = null;
            foreach (var s in supers)
            {
                if (sole is null && s.Account == "superAdmin")
                {
                    sole = s;
                    continue;
                }
                s.IsSuperAdmin = false;
                await users.UpdateAsync(s);
            }
            Assert.NotNull(sole);
            var (seed, _) = await MarkSuperAdminTotpReadyWithSeedAsync(scope.ServiceProvider);
            _ = seed;
        }

        var anon = f.CreateClient();
        // 错误 grant
        var bad = await (await anon.PostJson("/api/v1/auth/mfa/emergency-reset", new
        {
            emergencyGrant = "wrong-grant",
            account = "superAdmin",
            currentPassword = "Test@123456",
        })).ReadEnvelope();
        Assert.NotEqual(0, bad.GetProperty("code").GetInt32());

        var ok = await (await anon.PostJson("/api/v1/auth/mfa/emergency-reset", new
        {
            emergencyGrant = emergency,
            account = "superAdmin",
            currentPassword = "Test@123456",
        })).ReadEnvelope();
        Assert.Equal(0, ok.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(ok.GetProperty("data").GetProperty("invite").GetProperty("token").GetString()));

        // 重放 emergency grant
        var replay = await (await anon.PostJson("/api/v1/auth/mfa/emergency-reset", new
        {
            emergencyGrant = emergency,
            account = "superAdmin",
            currentPassword = "Test@123456",
        })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.BindInviteInvalid, replay.GetProperty("code").GetInt32());
    }

    /// <summary>InitGrant 超过 InitGrantTtlMinutes 后即使未消费也拒绝。</summary>
    [Fact]
    public async Task Init_grant_expires_after_configured_ttl()
    {
        var clock = new MfaFakeTimeProvider(DateTimeOffset.UtcNow);
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Level3:InitGrant"] = "test-init-grant-super-secret-token-32b",
                ["TenonAdmin:Security:Level3:InitGrantTtlMinutes"] = "30",
                ["TenonAdmin:Security:Level3:InitGrantNotAfter"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O"),
                ["TenonAdmin:Security:Level3:EmergencyGrant"] = "test-emergency-grant-super-secret-32b",
                ["TenonAdmin:Security:Level3:EmergencyGrantNotAfter"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O"),
                ["TenonAdmin:Security:DataProtection:Key"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["TenonAdmin:Security:DataProtection:KeyVersion"] = "1",
            },
            Overrides = s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(clock);
                s.Replace(ServiceDescriptor.Singleton<ISecurityProfileAccessor, StubLevel3Profile>());
                foreach (var d in s.ToList())
                {
                    if (d.ServiceType != typeof(IHostedService)) continue;
                    var name = d.ImplementationType?.Name
                               ?? d.ImplementationInstance?.GetType().Name
                               ?? "";
                    if (name.Contains("Level3Startup", StringComparison.Ordinal)
                        || name.Contains("Level3Idle", StringComparison.Ordinal))
                        s.Remove(d);
                }
            },
        };

        var (_, super, password) = await SeedUserAsync(f, superAdmin: true);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();

        // 首次观测:记录 first-seen,应成功启动绑定(密码正确)
        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Token = security.Level3.InitGrant!,
            CurrentPassword = password,
            Account = super.Account,
        });
        Assert.False(string.IsNullOrEmpty(start.BindChallengeId));

        // 超过 30 分钟 TTL
        clock.Advance(TimeSpan.FromMinutes(31));
        var expired = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.StartBindAsync(new TotpBindStartInput
            {
                Token = security.Level3.InitGrant!,
                CurrentPassword = password,
                Account = super.Account,
            }));
        Assert.Equal(ErrorCode.BindInviteInvalid, expired.Code);
    }

    /// <summary>
    /// StartBind 时 NotAfter 有效;将行内 AbsoluteNotAfterUtc 改到库「过去」后,
    /// CompleteBind 必须因 DB 执行时刻谓词失败且不写 MFA(独立于 first-seen TTL / 应用时钟)。
    /// </summary>
    [Fact]
    public async Task InitGrant_start_ok_then_complete_after_NotAfter_fails_without_binding()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, super, password) = await SeedUserAsync(f, superAdmin: true);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var grants = scope.ServiceProvider.GetRequiredService<IRepository<SysLevel3DeployGrant>>();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();

        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Token = security.Level3.InitGrant!,
            CurrentPassword = password,
            Account = super.Account,
        });
        Assert.False(string.IsNullOrEmpty(start.Seed));

        // 模拟「应用已过早失败检查、但提交时已过期」:固化截止改到数据库过去时刻
        // Consume 最终 WHERE 用 DB UTC 与 AbsoluteNotAfterUtc 比较,必须拒绝
        var row = await grants.GetFirstAsync(g => g.Kind == Level3DeployGrantKinds.Init);
        Assert.NotNull(row);
        row!.AbsoluteNotAfterUtc = DateTime.UtcNow.AddMinutes(-5);
        await grants.UpdateAsync(row);

        var code = totp.ComputeCode(start.Seed);
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.CompleteBindAsync(new TotpBindCompleteInput
            {
                BindChallengeId = start.BindChallengeId,
                TotpCode = code,
            }));
        Assert.Equal(ErrorCode.BindInviteInvalid, ex.Code);

        var after = await users.GetByIdAsync(super.Id);
        Assert.NotNull(after);
        Assert.False(after!.TotpEnabled);
        Assert.True(string.IsNullOrEmpty(after.TotpSeedProtected));

        var rowAfter = await grants.GetByIdAsync(row.Id);
        Assert.Null(rowAfter!.ConsumedAt);
    }

    /// <summary>
    /// 原子消费:确保行存在后把 AbsoluteNotAfterUtc 改到过去,Consume 必须以 DB 时刻谓词失败(affected=0)。
    /// </summary>
    [Fact]
    public async Task Deploy_grant_consume_rejects_when_AbsoluteNotAfter_already_past_at_db_time()
    {
        using var f = Factory(forceMfaProfile: true);
        using var scope = f.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ILevel3DeployGrantStore>();
        var grants = scope.ServiceProvider.GetRequiredService<IRepository<SysLevel3DeployGrant>>();
        var hash = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var notAfter = DateTimeOffset.UtcNow.AddHours(1);

        await store.EnsureWithinTtlAsync(Level3DeployGrantKinds.Init, hash, 60, notAfter);
        var row = await grants.GetFirstAsync(g => g.Kind == Level3DeployGrantKinds.Init && g.GrantHash == hash);
        Assert.NotNull(row);
        row!.AbsoluteNotAfterUtc = DateTime.UtcNow.AddSeconds(-30);
        await grants.UpdateAsync(row);

        await Assert.ThrowsAsync<AdminException>(() =>
            store.ConsumeAsync(Level3DeployGrantKinds.Init, hash, 60, notAfter));

        var again = await grants.GetByIdAsync(row.Id);
        Assert.Null(again!.ConsumedAt);
    }

    /// <summary>
    /// StartBind 后把 FirstSeenAt 改到超过 TTL 的过去:CompleteBind 须因 DB 执行时刻 TTL 谓词失败且不写 MFA。
    /// 不依赖应用侧预读 cutoff。
    /// </summary>
    [Fact]
    public async Task InitGrant_start_ok_then_complete_after_ttl_fails_without_binding()
    {
        using var f = Factory(forceMfaProfile: true);
        var (_, super, password) = await SeedUserAsync(f, superAdmin: true);
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var grants = scope.ServiceProvider.GetRequiredService<IRepository<SysLevel3DeployGrant>>();
        var security = scope.ServiceProvider.GetRequiredService<AdminSecurityOptions>();

        var start = await enroll.StartBindAsync(new TotpBindStartInput
        {
            Token = security.Level3.InitGrant!,
            CurrentPassword = password,
            Account = super.Account,
        });
        Assert.False(string.IsNullOrEmpty(start.Seed));

        // 将 first-seen 推到默认 TTL(60m 配置为 Factory 默认 InitGrantTtlMinutes=60)之外
        // AbsoluteNotAfter 仍未来 → 仅 TTL 谓词应拒绝
        var row = await grants.GetFirstAsync(g => g.Kind == Level3DeployGrantKinds.Init);
        Assert.NotNull(row);
        row!.FirstSeenAt = DateTime.UtcNow.AddMinutes(-90);
        await grants.UpdateAsync(row);

        var code = totp.ComputeCode(start.Seed);
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            enroll.CompleteBindAsync(new TotpBindCompleteInput
            {
                BindChallengeId = start.BindChallengeId,
                TotpCode = code,
            }));
        Assert.Equal(ErrorCode.BindInviteInvalid, ex.Code);

        var after = await users.GetByIdAsync(super.Id);
        Assert.NotNull(after);
        Assert.False(after!.TotpEnabled);
        Assert.True(string.IsNullOrEmpty(after.TotpSeedProtected));
        Assert.Null((await grants.GetByIdAsync(row.Id))!.ConsumedAt);
    }

    /// <summary>
    /// 原子消费:FirstSeenAt 改到过去后,Consume 必须以 DB 时刻 TTL 谓词失败(affected=0)。
    /// </summary>
    [Fact]
    public async Task Deploy_grant_consume_rejects_when_first_seen_ttl_expired_at_db_time()
    {
        using var f = Factory(forceMfaProfile: true);
        using var scope = f.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ILevel3DeployGrantStore>();
        var grants = scope.ServiceProvider.GetRequiredService<IRepository<SysLevel3DeployGrant>>();
        var hash = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var notAfter = DateTimeOffset.UtcNow.AddHours(2);
        const int ttl = 30;

        await store.EnsureWithinTtlAsync(Level3DeployGrantKinds.Init, hash, ttl, notAfter);
        var row = await grants.GetFirstAsync(g => g.Kind == Level3DeployGrantKinds.Init && g.GrantHash == hash);
        Assert.NotNull(row);
        row!.FirstSeenAt = DateTime.UtcNow.AddMinutes(-(ttl + 15));
        await grants.UpdateAsync(row);

        await Assert.ThrowsAsync<AdminException>(() =>
            store.ConsumeAsync(Level3DeployGrantKinds.Init, hash, ttl, notAfter));

        Assert.Null((await grants.GetByIdAsync(row.Id))!.ConsumedAt);
    }

    /// <summary>Level3 下未注入 ILevel3DeployGrantStore 时预检 critical。</summary>
    [Fact]
    public async Task Level3_precheck_fails_without_deploy_grant_store()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var security = new AdminSecurityOptions
        {
            Profile = SecurityProfile.Level3,
            DataProtection = new AdminDataProtectionOptions { Key = key, KeyVersion = 1 },
            Level3 = new AdminLevel3Options
            {
                InitGrant = "g",
                InitGrantNotAfter = DateTimeOffset.UtcNow.AddHours(1),
            },
        };
        var cacheOpts = new AdminCacheOptions
        {
            Provider = "Redis",
            RedisConnectionString = "redis://:pw@localhost:6379?ssl=true",
            RequireTls = true,
        };
        var env = new PrecheckEnv("Production");
        var accessor = new SecurityProfileAccessor(security, env);
        var policy = new SecurityPolicyProvider(
            new EmptyConfig(),
            security,
            new AdminJwtOptions());
        var svc = new Level3PrecheckService(
            accessor, security, cacheOpts, policy, env,
            users: null,
            cacheProvider: null,
            deployGrants: null);

        var result = await svc.RunAsync();
        var store = result.Checks.Single(c => c.Id == Level3PrecheckConstants.CheckDeployGrantStore);
        Assert.Equal(Level3CheckStatus.Fail, store.Status);
        Assert.True(store.Critical);
        Assert.Contains(Level3PrecheckConstants.CheckDeployGrantStore, result.CriticalFailureIds);
    }

    private sealed class PrecheckEnv(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class EmptyConfig : IConfigService
    {
        public Task<string?> GetValueByKeyAsync(string key) => Task.FromResult<string?>(null);
        public Task<PagedList<SysConfig>> PageAsync(ConfigPageInput input) => throw new NotImplementedException();
        public Task<SysConfig> GetAsync(long id) => throw new NotImplementedException();
        public Task<SiteInfoOutput> GetSiteInfoAsync() => throw new NotImplementedException();
        public Task SaveValuesAsync(IReadOnlyCollection<ConfigBatchItem> items) => throw new NotImplementedException();
        public Task<long> AddAsync(ConfigInput input) => throw new NotImplementedException();
        public Task UpdateAsync(long id, ConfigInput input) => throw new NotImplementedException();
        public Task DeleteAsync(long id) => throw new NotImplementedException();
    }
}

/// <summary>MFA 用例可控时钟(与 Level3SessionCsrfTests 同形)。</summary>
file sealed class MfaFakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utc;

    public MfaFakeTimeProvider(DateTimeOffset start) => _utc = start;

    public override DateTimeOffset GetUtcNow() => _utc;

    public void Advance(TimeSpan delta) => _utc += delta;
}
