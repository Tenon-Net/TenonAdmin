using System.Net;

namespace TenonAdmin.Tests;

/// <summary>
/// 请求限流回归(§12/§14)。认证端点(/api/v1/auth/*)按 IP 更严一档,超阈返回 429 + 统一信封(40008);
/// 非认证端点走宽松全局档。默认工厂关限流(隔离其他用例),本用例经 Settings 显式开并收紧阈值。
/// </summary>
public class RateLimitTests
{
    [Fact]
    public async Task Auth_endpoint_returns_429_envelope_after_exceeding_limit()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:RateLimit:Enabled"] = "true",
                ["TenonAdmin:Security:RateLimit:AuthPermitPerWindow"] = "2",
                ["TenonAdmin:Security:RateLimit:WindowSeconds"] = "60",
            },
        };
        var c = f.CreateClient();

        // 认证端点(匿名验证码)前 2 次放行,第 3 次超窗被限
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/captcha")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/captcha")).StatusCode);

        var limited = await c.GetAsync("/api/v1/auth/captcha");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(40008, (await limited.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Non_auth_endpoint_uses_lenient_global_limit()
    {
        // 认证端点限得很死(1),但非认证端点(/health)走宽松全局档,连打不被误伤
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:RateLimit:Enabled"] = "true",
                ["TenonAdmin:Security:RateLimit:AuthPermitPerWindow"] = "1",
                ["TenonAdmin:Security:RateLimit:PermitPerWindow"] = "100",
            },
        };
        var c = f.CreateClient();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Disabled_rate_limit_never_limits()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:RateLimit:Enabled"] = "false",
                ["TenonAdmin:Security:RateLimit:AuthPermitPerWindow"] = "1",
            },
        };
        var c = f.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/captcha")).StatusCode);
    }
}
