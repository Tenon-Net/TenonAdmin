using System.Net;
using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 分类配置中心的 HTTP 级回归:匿名站点信息端点 + 批量存值(只改已存在键、未知键忽略、缓存即时失效)。
/// site 端点匿名可读是"登录页/无配置权限用户也能取站点标题"的契约,单独锁死。
/// </summary>
public class ConfigCenterTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Site_info_is_anonymous_and_returns_seeded_title()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient(); // 不带 token

        var resp = await anon.GetAsync("/api/v1/sys/config/site");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // 未被默认拒绝策略挡成 401
        var env = await resp.ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        var data = env.GetProperty("data");
        Assert.Equal("TenonAdmin", data.GetProperty("title").GetString()); // ConfigSeed 播的站点标题
        // 品牌文字套装随 site 端点匿名下发(登录页页脚/副标题消费)
        Assert.Equal("TenonAdmin", data.GetProperty("copyright").GetString());
        Assert.Equal("", data.GetProperty("subtitle").GetString());
        Assert.Equal("", data.GetProperty("copyrightUrl").GetString());
    }

    [Fact]
    public async Task Batch_updates_existing_key_ignores_unknown_and_invalidates_cache()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);
        var anon = f.CreateClient();

        // 先读一次站点标题,让 GetValueByKeyAsync 把旧值写进读穿透缓存
        Assert.Equal("TenonAdmin", (await (await anon.GetAsync("/api/v1/sys/config/site")).ReadEnvelope())
            .GetProperty("data").GetProperty("title").GetString());

        // 批量存值:已存在键被更新;未知键静默忽略(不报错)
        var batch = await c.PutJson("/api/v1/sys/config/batch", new object[]
        {
            new { configKey = "sys.site.title", configValue = "榫卯后台" },
            new { configKey = "does.not.exist", configValue = "x" },
        });
        Assert.Equal(0, (await batch.ReadEnvelope()).GetProperty("code").GetInt32());

        // 缓存已失效:再读站点信息拿到新值(证明 InvalidateAsync 生效)
        Assert.Equal("榫卯后台", (await (await anon.GetAsync("/api/v1/sys/config/site")).ReadEnvelope())
            .GetProperty("data").GetProperty("title").GetString());

        // 未知键未被创建:按键分页查不到
        var page = await (await c.GetAsync("/api/v1/sys/config/page?Current=1&Size=50&ConfigKey=does.not.exist")).ReadEnvelope();
        Assert.Empty(page.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Page_excludes_structured_groups_without_hiding_custom_configs()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var add = await c.PostJson("/api/v1/sys/config", new
        {
            configKey = "custom.feature.enabled", configValue = "true", name = "自定义功能开关",
            groupCode = "custom", sort = 1,
        });
        Assert.Equal(0, (await add.ReadEnvelope()).GetProperty("code").GetInt32());

        var page = await (await c.GetAsync(
            "/api/v1/sys/config/page?Current=1&Size=50&ExcludedGroupCodes=sys&ExcludedGroupCodes=security&ExcludedGroupCodes=upload"))
            .ReadEnvelope();
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();

        Assert.Contains(items, item => item.GetProperty("configKey").GetString() == "custom.feature.enabled");
        Assert.DoesNotContain(items, item => item.GetProperty("groupCode").GetString() is "sys" or "security" or "upload");
    }
}
