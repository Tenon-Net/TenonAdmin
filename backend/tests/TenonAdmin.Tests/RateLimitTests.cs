using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 请求限流回归(§12/§14)。认证端点(/api/v1/auth/*)按 IP 更严一档,超阈返回 429 + 统一信封(40008);
/// 非认证端点走宽松全局档。开关/阈值<b>运行时可改</b>:阈值经 SysConfig 存值 + <see cref="RuntimeRateLimit"/> 快照读取。
/// Options <c>Enabled</c> 是硬总开关(默认工厂关之以隔离其他用例);为 true 时由 DB 值调控。
/// </summary>
public class RateLimitTests
{
    // 直接经 IConfigService 写 DB 值 + 确定性刷新快照(绕开事件总线的异步窗口),不需鉴权。
    private static async Task SetRateLimit(AdminAppFactory f, bool enabled, int? window = null, int? permit = null, int? authPermit = null)
    {
        using var scope = f.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var items = new List<ConfigBatchItem>
        {
            new() { ConfigKey = AdminRateLimitOptions.KEY_ENABLED, ConfigValue = enabled.ToString() },
        };
        if (window is int w) items.Add(new() { ConfigKey = AdminRateLimitOptions.KEY_WINDOW, ConfigValue = w.ToString() });
        if (permit is int p) items.Add(new() { ConfigKey = AdminRateLimitOptions.KEY_PERMIT, ConfigValue = p.ToString() });
        if (authPermit is int ap) items.Add(new() { ConfigKey = AdminRateLimitOptions.KEY_AUTH_PERMIT, ConfigValue = ap.ToString() });
        await config.SaveValuesAsync(items);
        await f.Services.GetRequiredService<RuntimeRateLimit>().RefreshAsync();
    }

    private static AdminAppFactory Enabled() =>
        new() { Settings = new Dictionary<string, string?> { ["TenonAdmin:Security:RateLimit:Enabled"] = "true" } };

    [Fact]
    public async Task Auth_endpoint_returns_429_envelope_after_exceeding_limit()
    {
        using var f = Enabled();
        // DB 收紧认证阈值到 2 → 证明限流阈值由 DB 值驱动(非 Options)
        await SetRateLimit(f, enabled: true, authPermit: 2, window: 60);
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
        // 认证端点限得很死(1),但非认证端点(/health)走宽松全局档(100),连打不被误伤
        using var f = Enabled();
        await SetRateLimit(f, enabled: true, authPermit: 1, permit: 100);
        var c = f.CreateClient();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Disabled_master_switch_never_limits()
    {
        // Options 硬总开关关:无论 DB 阈值多严都不限流(默认工厂即此配置)
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/captcha")).StatusCode);
    }

    [Fact]
    public async Task Runtime_toggle_off_via_config_stops_limiting()
    {
        // 主开关开 + DB 严限 → 触发限流;再经 DB 关闭限流并刷新 → 恢复放行(证明改配置运行时生效)
        using var f = Enabled();
        await SetRateLimit(f, enabled: true, authPermit: 1);
        var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/captcha")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/api/v1/auth/captcha")).StatusCode);

        await SetRateLimit(f, enabled: false);
        // 关闭后新请求不再被限(选择器读到 Enabled=false → 不限流分区)
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/captcha")).StatusCode);
    }
}
