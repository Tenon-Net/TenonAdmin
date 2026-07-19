using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 短信二次验证(MFA)全流程:密码过 → 40009 信令(带挑战)→ 凭挑战+短信码换令牌。
/// 覆盖:无手机号直通、错码计次作废、挑战单次消费、重发冷却。
/// </summary>
public class MfaLoginFlowTests
{
    /// <summary>捕获式短信通道:替换默认日志通道,测试从中取"发出"的验证码。</summary>
    internal sealed class CapturingSmsSender : ISmsSender
    {
        public string? LastPhone;
        public string? LastCode;
        public string? LastPurpose;
        public int SendCount;

        public Task SendCodeAsync(string phone, string code, string purpose, CancellationToken cancellationToken = default)
        {
            LastPhone = phone; LastCode = code; LastPurpose = purpose; SendCount++;
            return Task.CompletedTask;
        }
    }

    private static AdminAppFactory Factory(CapturingSmsSender sms) => new()
    {
        Overrides = s => s.Replace(ServiceDescriptor.Singleton<ISmsSender>(sms)),
    };

    /// <summary>开启 MFA 开关(运行时 DB 配置,同验证码开关成法)。</summary>
    private static async Task EnableMfa(AdminAppFactory f)
    {
        var admin = f.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await admin.LoginToken("superAdmin", "Test@123456"));
        var r = await admin.PutJson("/api/v1/sys/config/batch", new object[]
        {
            new { configKey = "sys.security.mfa.enabled", configValue = "true" },
        });
        Assert.Equal(0, (await r.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    /// <summary>给账号绑手机号(内核无个人手机验证流,测试直接落库)。</summary>
    private static async Task SetPhone(AdminAppFactory f, string account, string? phone)
    {
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var u = await users.GetFirstAsync(x => x.Account == account);
        u!.Phone = phone;
        await users.UpdateAsync(u);
    }

    [Fact]
    public async Task User_without_phone_logs_in_directly_even_when_mfa_enabled()
    {
        var sms = new CapturingSmsSender();
        using var f = Factory(sms);
        await EnableMfa(f);   // 种子超管无手机号 → 全局开关不锁死任何人

        var login = await (await f.CreateClient().PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal(0, login.GetProperty("code").GetInt32());
        Assert.Equal(0, sms.SendCount);
    }

    [Fact]
    public async Task Phone_bound_user_gets_challenge_then_completes_login_with_sms_code()
    {
        var sms = new CapturingSmsSender();
        using var f = Factory(sms);
        await EnableMfa(f);
        await SetPhone(f, "superAdmin", "13800001234");

        var c = f.CreateClient();
        var login = await (await c.PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();

        // 密码过 → 40009 信令,args 带挑战与打码手机号;短信已经捕获通道"发出"
        Assert.Equal(40009, login.GetProperty("code").GetInt32());
        var args = login.GetProperty("args");
        var challengeId = args.GetProperty("challengeId").GetString()!;
        Assert.Equal("138****1234", args.GetProperty("phoneMask").GetString());
        Assert.True(args.GetProperty("expiresSeconds").GetInt32() > 0);
        Assert.Equal("mfa", sms.LastPurpose);
        Assert.Equal("13800001234", sms.LastPhone);

        // 凭挑战 + 码换令牌
        var done = await (await c.PostJson("/api/v1/auth/login/sms",
            new { challengeId, code = sms.LastCode })).ReadEnvelope();
        Assert.Equal(0, done.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(done.GetProperty("data").GetProperty("accessToken").GetString()));

        // 挑战/码单次消费:同一对重放 → 40011
        var replay = await (await c.PostJson("/api/v1/auth/login/sms",
            new { challengeId, code = sms.LastCode })).ReadEnvelope();
        Assert.Equal(40011, replay.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Wrong_code_counts_attempts_then_invalidates_the_code()
    {
        var sms = new CapturingSmsSender();
        using var f = Factory(sms);
        await EnableMfa(f);
        await SetPhone(f, "superAdmin", "13800001234");

        var c = f.CreateClient();
        var login = await (await c.PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        var challengeId = login.GetProperty("args").GetProperty("challengeId").GetString()!;
        var wrong = sms.LastCode == "000000" ? "111111" : "000000";

        // 默认 MaxAttempts=5:前 5 次错码回 40010(attemptsLeft 递减),第 5 次后码作废
        for (var i = 1; i <= 5; i++)
        {
            var r = await (await c.PostJson("/api/v1/auth/login/sms", new { challengeId, code = wrong })).ReadEnvelope();
            Assert.Equal(40010, r.GetProperty("code").GetInt32());
            Assert.Equal(5 - i, r.GetProperty("args").GetProperty("attemptsLeft").GetInt32());
        }

        // 码已废:正确码也只能 40011,必须重走密码登录
        var after = await (await c.PostJson("/api/v1/auth/login/sms",
            new { challengeId, code = sms.LastCode })).ReadEnvelope();
        Assert.Equal(40011, after.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Resend_is_cooldown_gated_and_unknown_challenge_rejected()
    {
        var sms = new CapturingSmsSender();
        using var f = Factory(sms);
        await EnableMfa(f);
        await SetPhone(f, "superAdmin", "13800001234");

        var c = f.CreateClient();
        var login = await (await c.PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        var challengeId = login.GetProperty("args").GetProperty("challengeId").GetString()!;

        // 首发刚发过 → 冷却期内重发 40008
        var resend = await (await c.PostJson("/api/v1/auth/login/sms/resend", new { challengeId })).ReadEnvelope();
        Assert.Equal(40008, resend.GetProperty("code").GetInt32());
        Assert.Equal(1, sms.SendCount);

        // 伪造/过期挑战 → 40011(不泄露细节)
        var bogus = await (await c.PostJson("/api/v1/auth/login/sms/resend", new { challengeId = "deadbeef" })).ReadEnvelope();
        Assert.Equal(40011, bogus.GetProperty("code").GetInt32());
        var bogusLogin = await (await c.PostJson("/api/v1/auth/login/sms",
            new { challengeId = "deadbeef", code = "123456" })).ReadEnvelope();
        Assert.Equal(40011, bogusLogin.GetProperty("code").GetInt32());
    }
}
