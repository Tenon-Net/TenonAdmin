using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 用户分页的角色反查(<c>?RoleId=</c>)。这是管理端"这个角色有哪些人"与"删角色前先报人数"的唯一数据来源——
/// 复用既有 <c>GET /sys/user/page</c>,不新增端点(权限码即路由,新端点会逼所有存量部署先授权新菜单)。
/// 空角色返回 total=0 单独立一条:实现里对"零持有者"做了显式短路(空 IN 列表在各方言下行为不一)。
/// </summary>
public class UserRoleFilterTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<long> AddRole(HttpClient c, string code)
    {
        var env = await (await c.PostJson("/api/v1/sys/role/add",
            new { name = code, code, sort = 0, enabled = true, remark = "" })).ReadEnvelope();
        return env.GetProperty("data").GetInt64();
    }

    private static async Task AddUser(HttpClient c, string account, params long[] roleIds) =>
        await (await c.PostJson("/api/v1/sys/user",
            new { account, password = "Test@123456", name = account, enabled = true, roleIds })).ReadEnvelope();

    /// <summary>取分页结果的 (total, 账号集合)。</summary>
    private static async Task<(int total, List<string> accounts)> Page(HttpClient c, string query)
    {
        var data = (await (await c.GetAsync($"/api/v1/sys/user/page?{query}")).ReadEnvelope()).GetProperty("data");
        return (
            data.GetProperty("total").GetInt32(),
            [.. data.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("account").GetString()!)]);
    }

    [Fact]
    public async Task Page_filtered_by_role_returns_only_holders()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var ops = await AddRole(c, "ops");
        var dev = await AddRole(c, "dev");
        await AddUser(c, "holder-a", ops);
        await AddUser(c, "holder-b", ops);
        await AddUser(c, "outsider", dev);   // 持另一个角色 —— 不该出现在 ops 的结果里

        var (total, accounts) = await Page(c, $"Current=1&Size=50&RoleId={ops}");

        Assert.Equal(2, total);   // total 就是删角色确认框里报的人数,必须准
        Assert.Equal(["holder-a", "holder-b"], accounts.Order());
    }

    [Fact]
    public async Task Page_filtered_by_role_with_no_holders_returns_empty()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var lonely = await AddRole(c, "lonely");   // 建了但没给任何人
        await AddUser(c, "somebody");              // 有用户,但不持这个角色

        var (total, accounts) = await Page(c, $"Current=1&Size=50&RoleId={lonely}");

        Assert.Equal(0, total);      // 不能因为"零持有者"就退化成不过滤(那会把全部用户报成该角色的人)
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task Page_without_role_filter_is_unaffected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var ops = await AddRole(c, "ops");
        await AddUser(c, "holder-a", ops);
        await AddUser(c, "nobody");

        var (total, accounts) = await Page(c, "Current=1&Size=50");

        Assert.Contains("holder-a", accounts);
        Assert.Contains("nobody", accounts);
        Assert.Contains("superAdmin", accounts);
        Assert.True(total >= 3, $"expected at least 3 users (seed + created), got {total}");
    }
}
