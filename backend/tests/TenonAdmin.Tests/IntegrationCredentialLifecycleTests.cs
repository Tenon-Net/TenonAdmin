using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using static TenonAdmin.Tests.IntegrationTestSupport;

namespace TenonAdmin.Tests;

/// <summary>
/// G02:接入应用与凭据生命周期。默认校验服务对缺失/错误/过期/撤销/应用停用全部判无效;到期边界与轮换并存窗口;
/// 两个宿主共享一个库时对同一应用状态给出一致结果;秘密不进入响应、库、日志与异常。
/// </summary>
public class IntegrationCredentialLifecycleTests
{
    [Fact]
    public async Task Default_validator_rejects_missing_malformed_unknown_wrong_revoked_disabled_and_deleted()
    {
        using var f = new IntegrationAppFactory();
        var (appId, issued) = await CreateAppWithKeyAsync(f.Services, "erp-a");

        var ok = await ValidateAsync(f.Services, issued.ApiKey);
        Assert.True(ok.IsValid);
        Assert.Equal(appId, ok.Identity!.AppId);
        Assert.Equal("erp-a", ok.Identity.AppCode);
        Assert.Equal(issued.Credential.Id, ok.Identity.CredentialId);
        Assert.Equal(issued.Credential.KeyId, ok.Identity.KeyId);
        Assert.Null(ok.Identity.OwnerOrgId);

        Assert.Equal(OpenAppCredentialFailure.Missing, (await ValidateAsync(f.Services, null)).Failure);
        Assert.Equal(OpenAppCredentialFailure.Missing, (await ValidateAsync(f.Services, "")).Failure);
        foreach (var malformed in new[]
        {
            "abc",
            issued.ApiKey + "x",
            "tnb_" + issued.ApiKey[4..],
            issued.ApiKey.ToUpperInvariant(),
            issued.ApiKey[..20] + "+" + issued.ApiKey[21..],
            issued.ApiKey[..^1] + "=",
        })
            Assert.Equal(OpenAppCredentialFailure.Malformed, (await ValidateAsync(f.Services, malformed)).Failure);

        var secret = issued.ApiKey[(4 + 16 + 1)..];
        var unknown = await ValidateAsync(f.Services, OpenAppApiKey.Format("0123456789abcdef", secret));
        Assert.Equal(OpenAppCredentialFailure.NotFound, unknown.Failure);
        Assert.Null(unknown.AppId);

        var otherSecret = new OpenAppKeyGenerator().Generate().Secret;
        var mismatch = await ValidateAsync(f.Services, OpenAppApiKey.Format(issued.Credential.KeyId, otherSecret));
        Assert.Equal(OpenAppCredentialFailure.SecretMismatch, mismatch.Failure);
        Assert.Equal(appId, mismatch.AppId);                 // 可识别凭据:调用记录能定位到应用
        Assert.Null(mismatch.Identity);

        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IOpenAppCredentialService>().RevokeAsync(appId, issued.Credential.Id));
        Assert.Equal(OpenAppCredentialFailure.Revoked, (await ValidateAsync(f.Services, issued.ApiKey)).Failure);
        // 重复撤销幂等,不改变结果
        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IOpenAppCredentialService>().RevokeAsync(appId, issued.Credential.Id));

        var (appB, keyB) = await CreateAppWithKeyAsync(f.Services, "erp-b");
        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IIntegrationAppService>().SetEnabledAsync(appB, false));
        Assert.Equal(OpenAppCredentialFailure.AppDisabled, (await ValidateAsync(f.Services, keyB.ApiKey)).Failure);
        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IIntegrationAppService>().SetEnabledAsync(appB, true));
        Assert.True((await ValidateAsync(f.Services, keyB.ApiKey)).IsValid);

        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IIntegrationAppService>().DeleteAsync(appB));
        var deleted = await ValidateAsync(f.Services, keyB.ApiKey);
        Assert.False(deleted.IsValid);
        Assert.Equal(OpenAppCredentialFailure.Revoked, deleted.Failure);   // 删除同事务撤销全部凭据
        await Assert.ThrowsAsync<AdminException>(() =>
            InScopeAsync(f.Services, sp => sp.GetRequiredService<IIntegrationAppService>().GetAsync(appB)));
    }

    [Fact]
    public async Task Only_revoked_credentials_can_be_deleted_and_audit_row_remains()
    {
        using var f = new IntegrationAppFactory();
        var (appId, issued) = await CreateAppWithKeyAsync(f.Services, "delete-key");
        var (otherAppId, _) = await CreateAppWithKeyAsync(f.Services, "other-key");
        var credentialId = issued.Credential.Id;

        using var scope = f.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpenAppCredentialService>();
        await ExpectCodeAsync(IntegrationErrorCode.CredentialNotRevoked, () => service.DeleteAsync(appId, credentialId));
        await ExpectCodeAsync(IntegrationErrorCode.CredentialNotFound, () => service.DeleteAsync(otherAppId, credentialId));
        Assert.True((await ValidateAsync(f.Services, issued.ApiKey)).IsValid);

        await service.RevokeAsync(appId, credentialId);
        await service.DeleteAsync(appId, credentialId);
        Assert.Empty(await service.ListAsync(appId));
        Assert.Equal(OpenAppCredentialFailure.NotFound, (await ValidateAsync(f.Services, issued.ApiKey)).Failure);
        await ExpectCodeAsync(IntegrationErrorCode.CredentialNotFound, () => service.DeleteAsync(appId, credentialId));

        var stored = await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>()
            .Queryable<IntegrationAppCredential>().ClearFilter<ISoftDelete>()
            .Where(c => c.Id == credentialId).SingleAsync();
        Assert.True(stored.IsDelete);
        Assert.NotNull(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task Expiry_boundary_is_closed_at_the_expiry_instant()
    {
        var clock = NewClock();
        using var f = new IntegrationAppFactory { Overrides = UseClock(clock) };
        var expiresAt = clock.GetUtcNow().AddHours(1);
        var (_, issued) = await CreateAppWithKeyAsync(f.Services, "expiry", new OpenAppCredentialCreateInput { ExpiresAt = expiresAt });
        Assert.Equal(expiresAt, issued.Credential.ExpiresAt);

        Assert.True((await ValidateAsync(f.Services, issued.ApiKey)).IsValid);
        clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));
        Assert.True((await ValidateAsync(f.Services, issued.ApiKey)).IsValid);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(OpenAppCredentialFailure.Expired, (await ValidateAsync(f.Services, issued.ApiKey)).Failure);
    }

    [Fact]
    public async Task Default_lifetime_explicit_expiry_rules_and_expiry_adjustment()
    {
        var clock = NewClock();
        using var f = new IntegrationAppFactory { Overrides = UseClock(clock) };
        var now = clock.GetUtcNow();
        var (appId, byDefault) = await CreateAppWithKeyAsync(f.Services, "lifetime");
        Assert.Equal(now.AddDays(365), byDefault.Credential.ExpiresAt);

        using var scope = f.Services.CreateScope();
        var creds = scope.ServiceProvider.GetRequiredService<IOpenAppCredentialService>();
        var never = await creds.CreateAsync(appId, new OpenAppCredentialCreateInput { NeverExpires = true, Name = "长期" });
        Assert.Null(never.Credential.ExpiresAt);
        Assert.Equal("长期", never.Credential.Name);

        await ExpectCodeAsync(IntegrationErrorCode.CredentialExpiryInvalid,
            () => creds.CreateAsync(appId, new OpenAppCredentialCreateInput { ExpiresAt = now.AddMinutes(-1) }));
        await ExpectCodeAsync(IntegrationErrorCode.CredentialExpiryInvalid,
            () => creds.CreateAsync(appId, new OpenAppCredentialCreateInput { ExpiresAt = now.AddDays(3651) }));

        // 缩短到期:立即按新时间判定
        await creds.SetExpiryAsync(appId, never.Credential.Id, new OpenAppCredentialExpiryInput { ExpiresAt = now.AddMinutes(10) });
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(OpenAppCredentialFailure.Expired, (await ValidateAsync(f.Services, never.ApiKey)).Failure);
        // 已过期(未撤销)的凭据允许延长,恢复有效
        await creds.SetExpiryAsync(appId, never.Credential.Id, new OpenAppCredentialExpiryInput { NeverExpires = true });
        Assert.True((await ValidateAsync(f.Services, never.ApiKey)).IsValid);
        await ExpectCodeAsync(IntegrationErrorCode.CredentialExpiryInvalid,
            () => creds.SetExpiryAsync(appId, never.Credential.Id, new OpenAppCredentialExpiryInput()));

        // 已撤销的凭据不可调整
        await creds.RevokeAsync(appId, never.Credential.Id);
        await ExpectCodeAsync(IntegrationErrorCode.CredentialNotActive,
            () => creds.SetExpiryAsync(appId, never.Credential.Id, new OpenAppCredentialExpiryInput { NeverExpires = true }));
    }

    [Fact]
    public async Task Rotation_keeps_old_and_new_valid_inside_window_then_old_expires_at_boundary()
    {
        var clock = NewClock();
        using var f = new IntegrationAppFactory { Overrides = UseClock(clock) };
        var (appId, oldKey) = await CreateAppWithKeyAsync(f.Services, "rotate");

        OpenAppCredentialIssued newKey;
        using (var scope = f.Services.CreateScope())
            newKey = await scope.ServiceProvider.GetRequiredService<IOpenAppCredentialService>()
                .RotateAsync(appId, oldKey.Credential.Id, new OpenAppCredentialRotateInput { OverlapHours = 2 });

        Assert.NotEqual(oldKey.ApiKey, newKey.ApiKey);
        Assert.Equal(oldKey.Credential.Id, newKey.Credential.RotatedFromId);
        Assert.True((await ValidateAsync(f.Services, oldKey.ApiKey)).IsValid);
        Assert.True((await ValidateAsync(f.Services, newKey.ApiKey)).IsValid);

        clock.Advance(TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
        Assert.True((await ValidateAsync(f.Services, oldKey.ApiKey)).IsValid);
        Assert.True((await ValidateAsync(f.Services, newKey.ApiKey)).IsValid);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(OpenAppCredentialFailure.Expired, (await ValidateAsync(f.Services, oldKey.ApiKey)).Failure);
        Assert.True((await ValidateAsync(f.Services, newKey.ApiKey)).IsValid);

        using var s2 = f.Services.CreateScope();
        var creds = s2.ServiceProvider.GetRequiredService<IOpenAppCredentialService>();
        // 过期的旧凭据不能再轮换
        await ExpectCodeAsync(IntegrationErrorCode.CredentialNotActive,
            () => creds.RotateAsync(appId, oldKey.Credential.Id, new OpenAppCredentialRotateInput()));
        await ExpectCodeAsync(IntegrationErrorCode.RotationOverlapInvalid,
            () => creds.RotateAsync(appId, newKey.Credential.Id, new OpenAppCredentialRotateInput { OverlapHours = 721 }));

        // 并存窗口为 0:旧凭据立即失效
        var third = await creds.RotateAsync(appId, newKey.Credential.Id, new OpenAppCredentialRotateInput { OverlapHours = 0 });
        Assert.Equal(OpenAppCredentialFailure.Expired, (await ValidateAsync(f.Services, newKey.ApiKey)).Failure);
        Assert.True((await ValidateAsync(f.Services, third.ApiKey)).IsValid);

        // 旧凭据本就更早到期时,轮换不会把它延长
        var shortKey = await creds.CreateAsync(appId, new OpenAppCredentialCreateInput { ExpiresAt = clock.GetUtcNow().AddMinutes(30) });
        await creds.RotateAsync(appId, shortKey.Credential.Id, new OpenAppCredentialRotateInput { OverlapHours = 24 });
        var list = await creds.ListAsync(appId);
        Assert.Equal(clock.GetUtcNow().AddMinutes(30), list.Single(c => c.Id == shortKey.Credential.Id).ExpiresAt);
    }

    [Fact]
    public async Task Active_credential_limit_blocks_create_and_allows_one_extra_only_during_rotation()
    {
        using var f = new IntegrationAppFactory
        {
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Integration:Credentials:MaxActivePerApp"] = "2" },
        };
        var (appId, first) = await CreateAppWithKeyAsync(f.Services, "limited");
        using var scope = f.Services.CreateScope();
        var creds = scope.ServiceProvider.GetRequiredService<IOpenAppCredentialService>();
        await creds.CreateAsync(appId, new OpenAppCredentialCreateInput());
        await ExpectCodeAsync(IntegrationErrorCode.CredentialLimitExceeded, () => creds.CreateAsync(appId, new OpenAppCredentialCreateInput()));

        var rotated = await creds.RotateAsync(appId, first.Credential.Id, new OpenAppCredentialRotateInput { OverlapHours = 1 });
        await ExpectCodeAsync(IntegrationErrorCode.CredentialLimitExceeded,
            () => creds.RotateAsync(appId, rotated.Credential.Id, new OpenAppCredentialRotateInput { OverlapHours = 1 }));

        await creds.RevokeAsync(appId, first.Credential.Id);
        await creds.RevokeAsync(appId, rotated.Credential.Id);
        await creds.CreateAsync(appId, new OpenAppCredentialCreateInput());   // 撤销释放名额
    }

    [Fact]
    public async Task Last_used_is_written_at_most_once_per_interval()
    {
        var clock = NewClock();
        using var f = new IntegrationAppFactory { Overrides = UseClock(clock) };
        var (appId, issued) = await CreateAppWithKeyAsync(f.Services, "touch");

        async Task<DateTimeOffset?> LastUsed()
        {
            using var scope = f.Services.CreateScope();
            var list = await scope.ServiceProvider.GetRequiredService<IOpenAppCredentialService>().ListAsync(appId);
            return list.Single().LastUsedAt;
        }

        Assert.Null(await LastUsed());
        var t0 = clock.GetUtcNow();
        Assert.True((await ValidateAsync(f.Services, issued.ApiKey)).IsValid);
        Assert.Equal(t0, await LastUsed());

        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.True((await ValidateAsync(f.Services, issued.ApiKey)).IsValid);
        Assert.Equal(t0, await LastUsed());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await ValidateAsync(f.Services, issued.ApiKey)).IsValid);
        Assert.Equal(t0.AddMinutes(5), await LastUsed());
    }

    [Fact]
    public async Task Two_hosts_sharing_one_database_agree_on_revocation_disable_and_enable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-itg-multi-{Guid.NewGuid():N}.db");
        try
        {
            using var hostA = new IntegrationAppFactory { DbPath = dbPath, WorkerId = 11, DeleteDbOnDispose = false };
            _ = hostA.CreateClient();
            using var hostB = new IntegrationAppFactory { DbPath = dbPath, ResetDatabase = false, WorkerId = 12, DeleteDbOnDispose = false };
            _ = hostB.CreateClient();

            var (appId, key1) = await CreateAppWithKeyAsync(hostA.Services, "shared");
            Assert.True((await ValidateAsync(hostB.Services, key1.ApiKey)).IsValid);   // B 先把结果「看」一遍

            await InScopeAsync(hostA.Services, sp => sp.GetRequiredService<IOpenAppCredentialService>().RevokeAsync(appId, key1.Credential.Id));
            Assert.Equal(OpenAppCredentialFailure.Revoked, (await ValidateAsync(hostB.Services, key1.ApiKey)).Failure);
            Assert.Equal(OpenAppCredentialFailure.Revoked, (await ValidateAsync(hostA.Services, key1.ApiKey)).Failure);

            OpenAppCredentialIssued key2 = null!;
            await InScopeAsync(hostB.Services, async sp =>
                key2 = await sp.GetRequiredService<IOpenAppCredentialService>().CreateAsync(appId, new OpenAppCredentialCreateInput()));
            Assert.True((await ValidateAsync(hostA.Services, key2.ApiKey)).IsValid);

            await InScopeAsync(hostA.Services, sp => sp.GetRequiredService<IIntegrationAppService>().SetEnabledAsync(appId, false));
            Assert.Equal(OpenAppCredentialFailure.AppDisabled, (await ValidateAsync(hostB.Services, key2.ApiKey)).Failure);
            Assert.Equal(OpenAppCredentialFailure.AppDisabled, (await ValidateAsync(hostA.Services, key2.ApiKey)).Failure);

            await InScopeAsync(hostB.Services, sp => sp.GetRequiredService<IIntegrationAppService>().SetEnabledAsync(appId, true));
            Assert.True((await ValidateAsync(hostA.Services, key2.ApiKey)).IsValid);
            Assert.True((await ValidateAsync(hostB.Services, key2.ApiKey)).IsValid);
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
            AdminAppFactory.TryDeleteWorkerIdLockDir(dbPath);
        }
    }

    [Fact]
    public async Task Secret_never_appears_in_responses_database_operation_log_or_logs()
    {
        var logs = new CapturingLoggerProvider();
        using var f = new IntegrationAppFactory
        {
            Settings = new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Trace" },
            Overrides = s => s.AddSingleton<ILoggerProvider>(logs),
        };
        var admin = await AuthProbe.SuperAdmin(f);

        var created = await (await admin.PostJson("/api/v1/integration/app", new { code = "leak-probe", name = "泄露探针" })).ReadEnvelope();
        Assert.Equal(0, created.GetProperty("code").GetInt32());
        var appId = created.GetProperty("data").GetInt64();

        var issueResp = await admin.PostJson($"/api/v1/integration/app/{appId}/credentials", new { name = "prod" });
        Assert.Contains("no-store", issueResp.Headers.CacheControl?.ToString() ?? "");
        var issued = await issueResp.ReadEnvelope();
        Assert.Equal(0, issued.GetProperty("code").GetInt32());
        var apiKey = issued.GetProperty("data").GetProperty("apiKey").GetString()!;
        var credentialId = issued.GetProperty("data").GetProperty("credential").GetProperty("id").GetInt64();
        Assert.True(OpenAppApiKey.TryParse(apiKey, out var keyId, out var secret));
        Assert.DoesNotContain("secretHash", issued.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var rotateResp = await admin.PostJson($"/api/v1/integration/app/{appId}/credentials/{credentialId}/rotate", new { overlapHours = 1 });
        var rotated = await rotateResp.ReadEnvelope();
        var rotatedKey = rotated.GetProperty("data").GetProperty("apiKey").GetString()!;
        Assert.True(OpenAppApiKey.TryParse(rotatedKey, out _, out var rotatedSecret));

        // 查询接口:只见公开标识,不见秘密或摘要
        foreach (var path in new[] { "/api/v1/integration/app/page", $"/api/v1/integration/app/{appId}", $"/api/v1/integration/app/{appId}/credentials" })
        {
            var body = await (await admin.GetAsync(path)).Content.ReadAsStringAsync();
            if (!path.EndsWith("/page", StringComparison.Ordinal)) Assert.Contains(keyId, body);
            Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain(rotatedSecret, body);
            Assert.DoesNotContain("secretHash", body, StringComparison.OrdinalIgnoreCase);
        }

        // 校验路径(有效 / 秘密错误 / 格式错误)不抛异常、不记原文
        Assert.True((await ValidateAsync(f.Services, apiKey)).IsValid);
        Assert.False((await ValidateAsync(f.Services, OpenAppApiKey.Format(keyId, new OpenAppKeyGenerator().Generate().Secret))).IsValid);
        Assert.False((await ValidateAsync(f.Services, apiKey + "garbage")).IsValid);
        await admin.PostJson($"/api/v1/integration/app/{appId}/credentials/{credentialId}/revoke", new { });

        // 库:只有摘要;任何表(凭据、操作日志)都不含秘密原文
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var raw = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            var credentialRows = JsonSerializer.Serialize(await db.Queryable<IntegrationAppCredential>().ToListAsync(), raw);
            var opLogs = JsonSerializer.Serialize(await db.Queryable<SysOpLog>().ToListAsync(), raw);
            foreach (var text in new[] { credentialRows, opLogs })
            {
                Assert.DoesNotContain(secret, text);
                Assert.DoesNotContain(rotatedSecret, text);
            }
            Assert.Contains("发放接入凭据", opLogs);
            Assert.Contains("轮换接入凭据", opLogs);
            Assert.Contains("撤销接入凭据", opLogs);
            Assert.Contains(new OpenAppKeyGenerator().ComputeSecretHash(secret), credentialRows);
        }

        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(secret, logs.AllText);
        Assert.DoesNotContain(rotatedSecret, logs.AllText);

        // 调试输出同样不带秘密
        var generated = new OpenAppKeyGenerator().Generate();
        Assert.DoesNotContain(generated.Secret, generated.ToString());
        Assert.DoesNotContain(apiKey, new OpenAppCredentialIssued { ApiKey = apiKey }.ToString());
    }

    [Fact]
    public void Identity_carrier_is_a_system_identity_without_user_claims()
    {
        var identity = new OpenAppIdentity(42, "erp-a", 7, "0123456789abcdef", 1001, 3);
        var principal = identity.ToPrincipal("TenonOpenApp");

        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal("erp-a", principal.Identity.Name);
        Assert.Null(principal.FindFirst("sub"));
        Assert.Null(principal.FindFirst(TokenClaimNames.SESSION_ID));
        Assert.Null(principal.FindFirst(TokenClaimNames.SUPER_ADMIN));
        Assert.Equal(identity, OpenAppIdentity.FromPrincipal(principal));

        // 内核的当前用户读取同一主体:无用户 Id、非超管、归属机构来自应用(审计锚点)
        var http = new DefaultHttpContext { User = principal };
        var current = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = http });
        Assert.True(current.IsAuthenticated);
        Assert.Null(current.UserId);
        Assert.Null(current.SessionId);
        Assert.False(current.IsSuperAdmin);
        Assert.Equal(1001, current.OrgId);

        var noOrg = new OpenAppIdentity(42, "erp-a", 7, "0123456789abcdef", null, 3).ToPrincipal("TenonOpenApp");
        Assert.Null(noOrg.FindFirst(TokenClaimNames.ORG_ID));
        Assert.Null(OpenAppIdentity.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(OpenAppIdentity.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "1")], "Bearer"))));
    }

    [Fact]
    public void Key_format_is_strict_and_generator_output_round_trips()
    {
        var generator = new OpenAppKeyGenerator();
        var a = generator.Generate();
        var b = generator.Generate();
        Assert.NotEqual(a.KeyId, b.KeyId);
        Assert.NotEqual(a.Secret, b.Secret);
        Assert.Equal(OpenAppApiKey.Length, a.ApiKey.Length);
        Assert.StartsWith("tna_", a.ApiKey);
        Assert.True(OpenAppApiKey.TryParse(a.ApiKey, out var keyId, out var secret));
        Assert.Equal(a.KeyId, keyId);
        Assert.Equal(a.Secret, secret);
        Assert.Equal(a.SecretHash, generator.ComputeSecretHash(secret));
        Assert.Equal(64, a.SecretHash.Length);

        Assert.False(OpenAppApiKey.TryParse(null, out _, out _));
        Assert.False(OpenAppApiKey.TryParse(a.ApiKey.Replace('.', '_'), out _, out _));
        Assert.False(OpenAppApiKey.TryParse("tna_" + a.KeyId.ToUpperInvariant() + a.ApiKey[20..], out var k, out var s));
        Assert.Equal("", k);
        Assert.Equal("", s);
    }

    [Fact]
    public async Task App_input_validation_code_uniqueness_and_owner_org()
    {
        using var f = new IntegrationAppFactory();
        using var scope = f.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IIntegrationAppService>();

        await ExpectCodeAsync(IntegrationErrorCode.AppInputInvalid, () => apps.AddAsync(new IntegrationAppCreateInput { Code = "a", Name = "x" }));
        await ExpectCodeAsync(IntegrationErrorCode.AppInputInvalid, () => apps.AddAsync(new IntegrationAppCreateInput { Code = "-bad", Name = "x" }));
        await ExpectCodeAsync(IntegrationErrorCode.AppInputInvalid, () => apps.AddAsync(new IntegrationAppCreateInput { Code = "ok-code", Name = " " }));
        await ExpectCodeAsync(IntegrationErrorCode.AppInputInvalid, () => apps.AddAsync(new IntegrationAppCreateInput { Code = "ok-code", Name = "x", RateLimitPerMinute = -1 }));
        await ExpectCodeAsync(IntegrationErrorCode.OwnerOrgNotFound, () => apps.AddAsync(new IntegrationAppCreateInput { Code = "ok-code", Name = "x", OwnerOrgId = 987654321 }));

        var orgId = await scope.ServiceProvider.GetRequiredService<IOrgService>().AddAsync(new OrgInput { Name = "对接部", Code = "itg-org-" + Guid.NewGuid().ToString("N")[..6], ParentId = 0 });
        var id = await apps.AddAsync(new IntegrationAppCreateInput { Code = "ok-code", Name = "x", OwnerOrgId = orgId, RateLimitPerMinute = 0 });
        await ExpectCodeAsync(IntegrationErrorCode.AppCodeExists, () => apps.AddAsync(new IntegrationAppCreateInput { Code = "ok-code", Name = "y" }));

        var detail = await apps.GetAsync(id);
        Assert.Equal(orgId, detail.OwnerOrgId);
        Assert.Equal("对接部", detail.OwnerOrgName);
        Assert.Equal(0, detail.RateLimitPerMinute);

        // 删除后编码可复用(软删释放唯一位)
        await apps.DeleteAsync(id);
        await apps.AddAsync(new IntegrationAppCreateInput { Code = "ok-code", Name = "z" });

        var page = await apps.PageAsync(new IntegrationAppPageInput { Keyword = "ok-" });
        Assert.Single(page.Items);
        Assert.Equal("z", page.Items[0].Name);
    }

    [Fact]
    public async Task State_version_increments_on_runtime_relevant_changes_only()
    {
        using var f = new IntegrationAppFactory();
        using var scope = f.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IIntegrationAppService>();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        async Task<long> Version(long appId) =>
            await db.Queryable<IntegrationApp>().ClearFilter().Where(a => a.Id == appId).Select(a => a.StateVersion).FirstAsync();

        var id = await apps.AddAsync(new IntegrationAppCreateInput { Code = "ver", Name = "v" });
        var v0 = await Version(id);
        await apps.SetEnabledAsync(id, true);                      // 无变化:不递增
        Assert.Equal(v0, await Version(id));
        await apps.SetEnabledAsync(id, false);
        Assert.Equal(v0 + 1, await Version(id));
        await apps.UpdateAsync(id, new IntegrationAppUpdateInput { Name = "v2", RateLimitPerMinute = 10 });
        Assert.Equal(v0 + 2, await Version(id));
        await apps.DeleteAsync(id);
        Assert.Equal(v0 + 3, await Version(id));
    }

    [Fact]
    public async Task Stop_loss_actions_survive_writes_based_on_a_stale_read()
    {
        using var f = new IntegrationAppFactory();
        var (appId, _) = await CreateAppWithKeyAsync(f.Services, "stale-app");
        var (credApp, first) = await CreateAppWithKeyAsync(f.Services, "stale-cred");
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ISqlSugarClient>();
        var appService = sp.GetRequiredService<IIntegrationAppService>();
        var credentialService = sp.GetRequiredService<IOpenAppCredentialService>();
        var second = await credentialService.CreateAsync(credApp, new OpenAppCredentialCreateInput());
        Task<IntegrationApp> LoadApp() => db.Queryable<IntegrationApp>().ClearFilter().Where(a => a.Id == appId).FirstAsync();
        Task<IntegrationAppCredential> LoadCredential(long id) => db.Queryable<IntegrationAppCredential>().Where(c => c.Id == id).FirstAsync();

        // 改名称时手里是停用之前读到的实体:停用保留,版本只增不减(整行回写会把 Enabled 与旧版本号写回去)
        var v0 = (await LoadApp()).StateVersion;
        var renamer = ActivatorUtilities.CreateInstance<StaleAppService>(sp, (Func<Task>)(() => appService.SetEnabledAsync(appId, false)));
        await renamer.UpdateAsync(appId, new IntegrationAppUpdateInput { Name = "新名称" });
        var renamed = await LoadApp();
        Assert.Equal("新名称", renamed.Name);
        Assert.False(renamed.Enabled);
        Assert.Equal(v0 + 2, renamed.StateVersion);

        // 启用时手里是删除之前读到的实体:删除保留(整行回写会把 IsDelete 写回去,应用就复活了)
        var enabler = ActivatorUtilities.CreateInstance<StaleAppService>(sp, (Func<Task>)(() => appService.DeleteAsync(appId)));
        await ExpectCodeAsync(IntegrationErrorCode.AppNotFound, () => enabler.SetEnabledAsync(appId, true));
        Assert.True((await LoadApp()).IsDelete);

        // 轮换、调整到期时手里是撤销之前读到的凭据:撤销保留,也不会从已撤销的凭据轮换出新凭据
        var rotator = ActivatorUtilities.CreateInstance<StaleCredentialService>(sp,
            (Func<Task>)(() => credentialService.RevokeAsync(credApp, first.Credential.Id)));
        await ExpectCodeAsync(IntegrationErrorCode.CredentialNotActive,
            () => rotator.RotateAsync(credApp, first.Credential.Id, new OpenAppCredentialRotateInput()));
        Assert.NotNull((await LoadCredential(first.Credential.Id)).RevokedAtUtc);
        Assert.Equal(2, await db.Queryable<IntegrationAppCredential>().CountAsync(c => c.AppId == credApp));   // 轮换整体回滚

        var extender = ActivatorUtilities.CreateInstance<StaleCredentialService>(sp,
            (Func<Task>)(() => credentialService.RevokeAsync(credApp, second.Credential.Id)));
        await ExpectCodeAsync(IntegrationErrorCode.CredentialNotActive,
            () => extender.SetExpiryAsync(credApp, second.Credential.Id, new OpenAppCredentialExpiryInput { NeverExpires = true }));
        Assert.NotNull((await LoadCredential(second.Credential.Id)).RevokedAtUtc);
    }

    [Fact]
    public async Task A_metadata_only_update_never_writes_the_owner_org()
    {
        using var f = new IntegrationAppFactory();
        var ownerA = await IntegrationOpenApiTestKit.NewOrgAsync(f.Services, "原归属");
        var ownerB = await IntegrationOpenApiTestKit.NewOrgAsync(f.Services, "新归属");
        var (appId, _) = await CreateAppWithKeyAsync(f.Services, "owner-race", ownerOrgId: ownerA);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var appService = sp.GetRequiredService<IIntegrationAppService>();

        // 改名称时手里是超管改归属机构之前读到的实体:只改元数据的请求不写归属机构列,超管的改动保留
        var renamer = ActivatorUtilities.CreateInstance<StaleAppService>(sp,
            (Func<Task>)(() => appService.UpdateAsync(appId, new IntegrationAppUpdateInput { Name = "owner-race", OwnerOrgId = ownerB })));
        await renamer.UpdateAsync(appId, new IntegrationAppUpdateInput { Name = "只改名称", OwnerOrgId = ownerA });

        var app = await sp.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationApp>().Where(a => a.Id == appId).FirstAsync();
        Assert.Equal("只改名称", app.Name);
        Assert.Equal(ownerB, app.OwnerOrgId);
    }

    /// <summary>读到应用之后、写回之前,另一位管理员先改了它(止损或改归属机构):返回的是过期实体。</summary>
    private sealed class StaleAppService(
        IRepository<IntegrationApp> apps, IRepository<IntegrationAppCredential> credentials, TimeProvider time, Func<Task> concurrently)
        : IntegrationAppService(apps, credentials, time)
    {
        private bool _raced;

        protected override async Task<IntegrationApp> RequireAppAsync(long id, CancellationToken cancellationToken)
        {
            var app = await base.RequireAppAsync(id, cancellationToken);
            if (!_raced)
            {
                _raced = true;
                await concurrently();
            }
            return app;
        }
    }

    /// <summary>读到凭据之后、写回之前,另一位管理员先撤销了它:返回的是过期实体。</summary>
    private sealed class StaleCredentialService(
        IRepository<IntegrationApp> apps, IRepository<IntegrationAppCredential> credentials, IOpenAppKeyGenerator keys,
        IntegrationOptions options, TimeProvider time, Func<Task> concurrently)
        : OpenAppCredentialService(apps, credentials, keys, options, time)
    {
        private bool _raced;

        protected override async Task<IntegrationAppCredential> RequireCredentialAsync(long appId, long credentialId, CancellationToken cancellationToken)
        {
            var row = await base.RequireCredentialAsync(appId, credentialId, cancellationToken);
            if (!_raced)
            {
                _raced = true;
                await concurrently();
            }
            return row;
        }
    }
}
