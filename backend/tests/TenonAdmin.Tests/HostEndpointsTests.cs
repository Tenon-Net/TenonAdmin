using System.Net;

namespace TenonAdmin.Tests;

/// <summary>
/// 宿主级横切端点回归(§12/§13.6):健康检查存活/就绪、CORS 命名策略、OpenAPI 文档内容与模块禁用一致性。
/// </summary>
public class HostEndpointsTests
{
    [Fact]
    public async Task Health_liveness_and_readiness_return_200()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);          // 存活
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/ready")).StatusCode);    // DB + 缓存就绪
    }

    [Fact]
    public async Task Cors_named_policy_allows_configured_origin()
    {
        // P1-4:配置放行源后,带 Origin 的请求得到 Access-Control-Allow-Origin(经 IStartupFilter 挂载的全局命名策略)
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Api:Cors:AllowedOrigins:0"] = "https://admin.example.com" },
        };
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("Origin", "https://admin.example.com");

        var r = await c.GetAsync("/health");
        Assert.True(r.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("https://admin.example.com", r.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_default_denies_unconfigured_origin()
    {
        // 默认收紧:未配置任何源 → 不回 Access-Control-Allow-Origin
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("Origin", "https://evil.example.com");

        var r = await c.GetAsync("/health");
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OpenApi_doc_contains_envelope_and_excludes_disabled_module()
    {
        // §13.6 契约源:开发环境暴露 /openapi/v1.json;含统一信封字段;被禁模块(Upload)的路由缺席(P2-22)
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Upload"] };
        var c = f.CreateClient();
        var r = await c.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var json = await r.Content.ReadAsStringAsync();
        Assert.Contains("msgKey", json);                       // Result 信封结构进契约
        Assert.DoesNotContain("/api/v1/sys/file", json);       // 禁用 Upload 模块 → 文件路由不出现在文档
    }
}
