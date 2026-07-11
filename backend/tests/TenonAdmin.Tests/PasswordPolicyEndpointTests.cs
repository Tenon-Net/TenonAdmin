using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 有效密码策略只读端点(<c>GET /api/v1/sys/config/password-policy</c>,[ActiveSession])。
/// 任何登录用户可读,供改密/建用户页展示真实规则清单;策略经 config/batch 改动后端点即反映新值
/// (证明 DB 驱动 + 读穿透缓存失效)。
/// </summary>
public class PasswordPolicyEndpointTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Returns_default_policy_for_logged_in_user()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var data = (await (await c.GetAsync("/api/v1/sys/config/password-policy")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(8, data.GetProperty("minLength").GetInt32());
        Assert.True(data.GetProperty("requireUpper").GetBoolean());
        Assert.True(data.GetProperty("requireLower").GetBoolean());
        Assert.True(data.GetProperty("requireDigit").GetBoolean());
        Assert.False(data.GetProperty("requireSpecial").GetBoolean());
    }

    [Fact]
    // ponytail: 一个会随接线/DB 驱动破坏而失败的可跑检查。
    public async Task Reflects_runtime_policy_change()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var batch = await c.PutJson("/api/v1/sys/config/batch", new object[]
        {
            new { configKey = "sys.security.password.minLength", configValue = "12" },
            new { configKey = "sys.security.password.requireSpecial", configValue = "true" },
        });
        Assert.Equal(0, (await batch.ReadEnvelope()).GetProperty("code").GetInt32());

        var data = (await (await c.GetAsync("/api/v1/sys/config/password-policy")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(12, data.GetProperty("minLength").GetInt32());
        Assert.True(data.GetProperty("requireSpecial").GetBoolean());
    }
}
