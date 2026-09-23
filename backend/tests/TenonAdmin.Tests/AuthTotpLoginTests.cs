using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// <c>POST /api/v1/auth/login/totp</c> 的 HTTP 契约。
/// 服务层完成路径在外部登录用例里;这里锁控制器信封:未绑定 40020、已绑定 40018、错码不消费挑战、对码出票、重放 40019。
/// </summary>
public class AuthTotpLoginTests
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

    private static async Task<(string account, string password)> SeedForceTotpUser(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        const string password = "TestPass123!";
        var account = "totp_" + Guid.NewGuid().ToString("N")[..8];
        await users.InsertAsync(new SysUser
        {
            Account = account,
            Password = hasher.Hash(password),
            Name = "TOTP",
            Enabled = true,
            ForceTotp = true,
            MustChangePassword = false,
            LastPasswordChangeTime = DateTime.Now,
        });
        return (account, password);
    }

    private static async Task<string> Bind(AdminAppFactory f, string account, string password)
    {
        using var scope = f.Services.CreateScope();
        var enroll = scope.ServiceProvider.GetRequiredService<IMfaEnrollmentService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
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
        return start.Seed;
    }

    private static string Code(AdminAppFactory f, string seed)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITotpService>().ComputeCode(seed);
    }

    [Fact]
    public async Task Unbound_force_totp_login_is_40020_without_token()
    {
        await using var f = Factory();
        var (account, password) = await SeedForceTotpUser(f);

        var resp = await f.CreateClient().PostJson("/api/v1/auth/login", new { account, password });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var login = await resp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpNotBound, login.GetProperty("code").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, login.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Wrong_code_keeps_challenge_and_correct_code_issues_token_once()
    {
        await using var f = Factory();
        var (account, password) = await SeedForceTotpUser(f);
        var seed = await Bind(f, account, password);
        var anon = f.CreateClient();

        var loginResp = await anon.PostJson("/api/v1/auth/login", new { account, password });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpRequired, login.GetProperty("code").GetInt32());
        var challengeId = login.GetProperty("args").GetProperty("challengeId").GetString();
        Assert.False(string.IsNullOrEmpty(challengeId));
        Assert.True(login.GetProperty("args").GetProperty("expiresSeconds").GetInt32() >= 60);

        var right = Code(f, seed);
        var wrong = right == "000000" ? "111111" : "000000";
        var bad = await (await anon.PostJson("/api/v1/auth/login/totp",
            new { challengeId, code = wrong })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpWrong, bad.GetProperty("code").GetInt32());

        var done = await (await anon.PostJson("/api/v1/auth/login/totp",
            new { challengeId, code = right })).ReadEnvelope();
        Assert.Equal(0, done.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(done.GetProperty("data").GetProperty("accessToken").GetString()));
        Assert.Equal(account, done.GetProperty("data").GetProperty("account").GetString());

        var replay = await (await anon.PostJson("/api/v1/auth/login/totp",
            new { challengeId, code = right })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpWrong, replay.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Missing_or_unknown_challenge_is_40019()
    {
        await using var f = Factory();
        var anon = f.CreateClient();

        var empty = await (await anon.PostJson("/api/v1/auth/login/totp",
            new { challengeId = "", code = "" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpWrong, empty.GetProperty("code").GetInt32());

        var unknown = await (await anon.PostJson("/api/v1/auth/login/totp",
            new { challengeId = "deadbeef", code = "123456" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.TotpWrong, unknown.GetProperty("code").GetInt32());
    }
}
