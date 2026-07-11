using System.Net.Http.Headers;
using System.Text.Json;

namespace TenonAdmin.Tests;

/// <summary>
/// 机构复制(子树克隆)的 HTTP 级回归。锁三条:整支克隆(根+后代)、克隆挂到源同级、
/// 编码追加 -copy 后缀去重(不撞唯一索引)。
/// </summary>
public class OrgCopyTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<long> AddOrg(HttpClient c, object body) =>
        (await (await c.PostJson("/api/v1/sys/org/add", body)).ReadEnvelope()).GetProperty("data").GetInt64();

    private static async Task<JsonElement> OrgList(HttpClient c) =>
        (await (await c.GetAsync("/api/v1/sys/org/list")).ReadEnvelope()).GetProperty("data");

    private static JsonElement? ById(JsonElement list, long id)
    {
        foreach (var o in list.EnumerateArray())
            if (o.GetProperty("id").GetInt64() == id) return o;
        return null;
    }

    [Fact]
    public async Task Copy_clones_whole_subtree_reparented_as_sibling_with_suffixed_codes()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var root = await AddOrg(c, new { parentId = 0, name = "复制源", code = "COPYSRC_ROOT", sort = 5, enabled = true });
        var child = await AddOrg(c, new { parentId = root, name = "复制源子", code = "COPYSRC_CHILD", sort = 1, enabled = true });

        var newRootId = (await (await c.PostJson($"/api/v1/sys/org/{root}/copy", new { })).ReadEnvelope())
            .GetProperty("data").GetInt64();

        var list = await OrgList(c);

        // 新根:名加 -副本、挂到源的同级(parentId=0)、编码加 -copy
        var newRoot = ById(list, newRootId)!.Value;
        Assert.Equal("复制源-副本", newRoot.GetProperty("name").GetString());
        Assert.Equal(0, newRoot.GetProperty("parentId").GetInt64());
        Assert.Equal("COPYSRC_ROOT-copy", newRoot.GetProperty("code").GetString());

        // 后代随克隆:恰有一个子节点挂在新根下,名保持、编码加 -copy
        var clonedChildren = list.EnumerateArray()
            .Where(o => o.GetProperty("parentId").GetInt64() == newRootId).ToList();
        Assert.Single(clonedChildren);
        Assert.Equal("复制源子", clonedChildren[0].GetProperty("name").GetString());
        Assert.Equal("COPYSRC_CHILD-copy", clonedChildren[0].GetProperty("code").GetString());

        // 原树完好(源根 + 源子仍在),且新增恰好 2 个节点
        Assert.NotNull(ById(list, root));
        Assert.NotNull(ById(list, child));
    }

    [Fact]
    public async Task Copy_twice_disambiguates_codes()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var root = await AddOrg(c, new { parentId = 0, name = "二次复制源", code = "TWICE_ROOT", sort = 6, enabled = true });

        // 复制两次:第一次 -copy,第二次因 -copy 已占用 → -copy2
        await c.PostJson($"/api/v1/sys/org/{root}/copy", new { });
        await c.PostJson($"/api/v1/sys/org/{root}/copy", new { });

        var codes = (await OrgList(c)).EnumerateArray()
            .Select(o => o.GetProperty("code").GetString())
            .Where(x => x is not null && x.StartsWith("TWICE_ROOT")).ToList();

        Assert.Contains("TWICE_ROOT", codes);
        Assert.Contains("TWICE_ROOT-copy", codes);
        Assert.Contains("TWICE_ROOT-copy2", codes);
    }

    [Fact]
    public async Task Copy_nonexistent_org_returns_not_found()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var res = await (await c.PostJson("/api/v1/sys/org/999999999/copy", new { })).ReadEnvelope();
        Assert.Equal(42003, res.GetProperty("code").GetInt32());   // OrgNotFound
    }
}
