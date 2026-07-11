using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 验证码开关运行时可配:默认关时登录直通;经 config/batch 开启后登录须过验证码,
/// 且匿名站点信息即时反映开关(证明后端读到 DB 新值 + 读穿透缓存已失效)。
/// </summary>
public class CaptchaConfigTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Disabled_by_default_login_passes_and_site_reports_false()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();

        var site = await (await c.GetAsync("/api/v1/sys/config/site")).ReadEnvelope();
        Assert.False(site.GetProperty("data").GetProperty("captchaEnabled").GetBoolean());

        // 默认关:不带验证码也能登录
        var login = await (await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal(0, login.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Enabling_at_runtime_forces_captcha_and_surfaces_in_site_info()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 运行时开启验证码
        var batch = await admin.PutJson("/api/v1/sys/config/batch", new object[]
        {
            new { configKey = "sys.security.captcha.enabled", configValue = "true" },
        });
        Assert.Equal(0, (await batch.ReadEnvelope()).GetProperty("code").GetInt32());

        var anon = f.CreateClient();

        // 站点信息即时反映开关为真(缓存已失效)
        var site = await (await anon.GetAsync("/api/v1/sys/config/site")).ReadEnvelope();
        Assert.True(site.GetProperty("data").GetProperty("captchaEnabled").GetBoolean());

        // 不带验证码登录 → CaptchaExpired(40002):后端读到 DB 新值并强制执行
        var login = await (await anon.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal(40002, login.GetProperty("code").GetInt32());
    }
}
