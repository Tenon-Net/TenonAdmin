using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 短信验证码免密登录全流程:发码 → 手机号+码换令牌。
/// 覆盖:开关闸门、防枚举(未知/重复手机号响应不可区分)、发送冷却、图形验证码联动、站点信息透出。
/// </summary>
public class SmsLoginFlowTests
{
    private static AdminAppFactory Factory(MfaLoginFlowTests.CapturingSmsSender sms) => new()
    {
        Overrides = s => s.Replace(ServiceDescriptor.Singleton<ISmsSender>(sms)),
    };

    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task SetConfig(AdminAppFactory f, string key, string value)
    {
        var admin = await SuperAdminClient(f);
        var r = await admin.PutJson("/api/v1/sys/config/batch", new object[] { new { configKey = key, configValue = value } });
        Assert.Equal(0, (await r.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    private static async Task SetPhone(AdminAppFactory f, string account, string? phone)
    {
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var u = await users.GetFirstAsync(x => x.Account == account);
        u!.Phone = phone;
        await users.UpdateAsync(u);
    }

    [Fact]
    public async Task Disabled_by_default_send_and_login_rejected_and_site_reports_false()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        var c = f.CreateClient();

        var site = await (await c.GetAsync("/api/v1/sys/config/site")).ReadEnvelope();
        Assert.False(site.GetProperty("data").GetProperty("smsLoginEnabled").GetBoolean());

        Assert.Equal(40012, (await (await c.PostJson("/api/v1/auth/sms/send",
            new { phone = "13800001234" })).ReadEnvelope()).GetProperty("code").GetInt32());
        Assert.Equal(40012, (await (await c.PostJson("/api/v1/auth/sms/login",
            new { phone = "13800001234", code = "123456" })).ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Send_then_login_with_code_succeeds_and_code_is_single_use()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.smsLogin.enabled", "true");
        await SetPhone(f, "superAdmin", "13800001234");

        var c = f.CreateClient();

        // 站点信息即时透出开关(登录页据此渲染短信入口)
        var site = await (await c.GetAsync("/api/v1/sys/config/site")).ReadEnvelope();
        Assert.True(site.GetProperty("data").GetProperty("smsLoginEnabled").GetBoolean());

        var send = await (await c.PostJson("/api/v1/auth/sms/send", new { phone = "13800001234" })).ReadEnvelope();
        Assert.Equal(0, send.GetProperty("code").GetInt32());
        Assert.True(send.GetProperty("data").GetProperty("expiresSeconds").GetInt32() > 0);
        Assert.Equal("login", sms.LastPurpose);

        var login = await (await c.PostJson("/api/v1/auth/sms/login",
            new { phone = "13800001234", code = sms.LastCode })).ReadEnvelope();
        Assert.Equal(0, login.GetProperty("code").GetInt32());
        Assert.Equal("superAdmin", login.GetProperty("data").GetProperty("account").GetString());

        // 码单次消费:重放 → 40011
        var replay = await (await c.PostJson("/api/v1/auth/sms/login",
            new { phone = "13800001234", code = sms.LastCode })).ReadEnvelope();
        Assert.Equal(40011, replay.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Unknown_phone_send_is_indistinguishable_and_login_fails_generically()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.smsLogin.enabled", "true");

        var c = f.CreateClient();

        // 未知手机号:响应形状与真实路径一致(防枚举),但确实没发短信
        var send = await (await c.PostJson("/api/v1/auth/sms/send", new { phone = "13999999999" })).ReadEnvelope();
        Assert.Equal(0, send.GetProperty("code").GetInt32());
        Assert.True(send.GetProperty("data").GetProperty("expiresSeconds").GetInt32() > 0);
        Assert.Equal(0, sms.SendCount);

        // 陪跑路径同样吃冷却:立刻再发 → 40008,与真实路径行为一致
        var again = await (await c.PostJson("/api/v1/auth/sms/send", new { phone = "13999999999" })).ReadEnvelope();
        Assert.Equal(40008, again.GetProperty("code").GetInt32());

        // 没码可验 → 通用 40011
        var login = await (await c.PostJson("/api/v1/auth/sms/login",
            new { phone = "13999999999", code = "123456" })).ReadEnvelope();
        Assert.Equal(40011, login.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// QA06:免密登录(手机号+码)错码时的响应,不能因手机号是否已注册而不同——
    /// 已知启用手机号错码与未知手机号错码都统一回 40011、且都不带 attemptsLeft(防枚举)。
    /// 底层验证码计次/达上限作废逻辑仍照常执行,只是对外观感一致。
    /// </summary>
    [Fact]
    public async Task Wrong_code_for_known_phone_normalizes_to_expired_without_attempts_left()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.smsLogin.enabled", "true");
        await SetPhone(f, "superAdmin", "13800001234");

        var c = f.CreateClient();
        var send = await (await c.PostJson("/api/v1/auth/sms/send", new { phone = "13800001234" })).ReadEnvelope();
        Assert.Equal(0, send.GetProperty("code").GetInt32());

        var wrong = sms.LastCode == "000000" ? "111111" : "000000";
        var login = await (await c.PostJson("/api/v1/auth/sms/login",
            new { phone = "13800001234", code = wrong })).ReadEnvelope();

        // 已知手机号错码:与未知手机号(见 Unknown_phone_send_is_indistinguishable_and_login_fails_generically)
        // 响应码完全一致——40011,且无 attemptsLeft(该字段只在 40010 场景出现)。
        Assert.Equal(40011, login.GetProperty("code").GetInt32());
        Assert.False(login.TryGetProperty("args", out var args) &&
            args.ValueKind == System.Text.Json.JsonValueKind.Object &&
            args.TryGetProperty("attemptsLeft", out _));

        // 正确码仍能登录成功(码未被此前的错码尝试作废——底层计次未到上限)
        var ok = await (await c.PostJson("/api/v1/auth/sms/login",
            new { phone = "13800001234", code = sms.LastCode })).ReadEnvelope();
        Assert.Equal(0, ok.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// QA06 对照组:密码登录后的短信二次验证挑战(已过密码校验,账号不再是秘密)不受免密登录归一影响——
    /// 错码继续回 40010 并带 attemptsLeft,行为与批次前一致。
    /// </summary>
    [Fact]
    public async Task Password_login_sms_challenge_keeps_wrong_code_with_attempts_left()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.mfa.enabled", "true");
        await SetPhone(f, "superAdmin", "13800001234");

        var c = f.CreateClient();
        var login = await (await c.PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal(40009, login.GetProperty("code").GetInt32());
        var challengeId = login.GetProperty("args").GetProperty("challengeId").GetString()!;

        var wrong = sms.LastCode == "000000" ? "111111" : "000000";
        var verify = await (await c.PostJson("/api/v1/auth/login/sms", new { challengeId, code = wrong })).ReadEnvelope();
        Assert.Equal(40010, verify.GetProperty("code").GetInt32());
        Assert.True(verify.GetProperty("args").GetProperty("attemptsLeft").GetInt32() >= 0);
    }

    [Fact]
    public async Task Duplicate_phone_users_are_silently_excluded_from_sms_login()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.smsLogin.enabled", "true");
        await SetPhone(f, "superAdmin", "13800001234");

        // 造一个同手机号的第二用户(存量库手机号无唯一约束,重复真实存在)
        var admin = await SuperAdminClient(f);
        var create = await admin.PostJson("/api/v1/sys/user", new
        {
            account = "dupPhone", name = "重复手机号", password = "Test@123456", phone = "13800001234", enabled = true,
            roleIds = Array.Empty<long>(),
        });
        Assert.Equal(0, (await create.ReadEnvelope()).GetProperty("code").GetInt32());

        // 命中 2 个 → 走陪跑:成功外形、不发码
        var c = f.CreateClient();
        var send = await (await c.PostJson("/api/v1/auth/sms/send", new { phone = "13800001234" })).ReadEnvelope();
        Assert.Equal(0, send.GetProperty("code").GetInt32());
        Assert.Equal(0, sms.SendCount);
    }

    [Fact]
    public async Task Resend_within_cooldown_is_rejected()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.smsLogin.enabled", "true");
        await SetPhone(f, "superAdmin", "13800001234");

        var c = f.CreateClient();
        Assert.Equal(0, (await (await c.PostJson("/api/v1/auth/sms/send",
            new { phone = "13800001234" })).ReadEnvelope()).GetProperty("code").GetInt32());

        var again = await (await c.PostJson("/api/v1/auth/sms/send", new { phone = "13800001234" })).ReadEnvelope();
        Assert.Equal(40008, again.GetProperty("code").GetInt32());
        Assert.Equal(60, again.GetProperty("args").GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal(1, sms.SendCount);
    }

    [Fact]
    public async Task Captcha_when_enabled_also_guards_the_send_endpoint()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.smsLogin.enabled", "true");
        await SetConfig(f, "sys.security.captcha.enabled", "true");
        await SetPhone(f, "superAdmin", "13800001234");

        // 不带图形验证码发码 → 40002(发码端点防脚本滥用)
        var send = await (await f.CreateClient().PostJson("/api/v1/auth/sms/send",
            new { phone = "13800001234" })).ReadEnvelope();
        Assert.Equal(40002, send.GetProperty("code").GetInt32());
        Assert.Equal(0, sms.SendCount);
    }

    [Fact]
    public async Task Disabled_user_gets_pretend_send_and_cannot_login()
    {
        var sms = new MfaLoginFlowTests.CapturingSmsSender();
        using var f = Factory(sms);
        await SetConfig(f, "sys.security.smsLogin.enabled", "true");

        var admin = await SuperAdminClient(f);
        var create = await admin.PostJson("/api/v1/sys/user", new
        {
            account = "disabledSms", name = "停用短信", password = "Test@123456", phone = "13700007777", enabled = false,
            roleIds = Array.Empty<long>(),
        });
        Assert.Equal(0, (await create.ReadEnvelope()).GetProperty("code").GetInt32());

        var c = f.CreateClient();
        var send = await (await c.PostJson("/api/v1/auth/sms/send", new { phone = "13700007777" })).ReadEnvelope();
        Assert.Equal(0, send.GetProperty("code").GetInt32());   // 防枚举:停用与不存在不可区分
        Assert.Equal(0, sms.SendCount);

        var login = await (await c.PostJson("/api/v1/auth/sms/login",
            new { phone = "13700007777", code = "123456" })).ReadEnvelope();
        Assert.Equal(40011, login.GetProperty("code").GetInt32());
    }
}
