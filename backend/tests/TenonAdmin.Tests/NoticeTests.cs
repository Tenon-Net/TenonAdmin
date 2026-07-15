using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 消息通知 HTTP 级回归:发布 → 未读数 → 我的(含已读标记)→ 标记已读 → 未读归零 → 删除。
/// 覆盖管理端 <c>[RolePermission]</c> 发布/删除 与用户端 <c>[ActiveSession]</c> 未读/我的/标记已读闭环。
/// </summary>
public class NoticeTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<HttpClient> ClientFor(AdminAppFactory f, string account, string pwd = "Test@123456")
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, pwd));
        return c;
    }

    private static async Task<long> AddRole(HttpClient c, string code) =>
        (await (await c.PostJson("/api/v1/sys/role/add",
            new { name = code, code, sort = 0, enabled = true, remark = "" })).ReadEnvelope())
            .GetProperty("data").GetInt64();

    private static async Task<long> AddUser(HttpClient c, string account, params long[] roleIds) =>
        (await (await c.PostJson("/api/v1/sys/user",
            new { account, password = "Test@123456", name = account, enabled = true, roleIds })).ReadEnvelope())
            .GetProperty("data").GetProperty("id").GetInt64();

    private static async Task<int> Unread(HttpClient c) =>
        (await (await c.GetAsync("/api/v1/sys/notice/unread-count")).ReadEnvelope()).GetProperty("data").GetInt32();

    private static async Task<List<string>> MineTitles(HttpClient c, string query = "Current=1&Size=50")
    {
        var data = (await (await c.GetAsync($"/api/v1/sys/notice/mine?{query}")).ReadEnvelope()).GetProperty("data");
        return [.. data.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("title").GetString()!)];
    }

    [Fact]
    public async Task Publish_unread_read_delete_cycle()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 初始未读为 0
        Assert.Equal(0, (await (await c.GetAsync("/api/v1/sys/notice/unread-count")).ReadEnvelope())
            .GetProperty("data").GetInt32());

        // 发布一条(type=2 公告)
        var pub = await (await c.PostJson("/api/v1/sys/notice", new { title = "维护通知", content = "今晚维护", type = 2 })).ReadEnvelope();
        Assert.Equal(0, pub.GetProperty("code").GetInt32());
        var id = pub.GetProperty("data").GetInt64();

        // 发布后未读 = 1(广播含发布者自身,未读)
        Assert.Equal(1, (await (await c.GetAsync("/api/v1/sys/notice/unread-count")).ReadEnvelope())
            .GetProperty("data").GetInt32());

        // 我的:含该条且 isRead=false
        var mine1 = await (await c.GetAsync("/api/v1/sys/notice/mine?Current=1&Size=10")).ReadEnvelope();
        var item1 = mine1.GetProperty("data").GetProperty("items").EnumerateArray().First();
        Assert.Equal("维护通知", item1.GetProperty("title").GetString());
        Assert.False(item1.GetProperty("isRead").GetBoolean());

        // 标记已读
        Assert.Equal(0, (await (await c.PutJson($"/api/v1/sys/notice/{id}/read", new { })).ReadEnvelope())
            .GetProperty("code").GetInt32());

        // 已读后未读归零(证明已读回执生效)
        Assert.Equal(0, (await (await c.GetAsync("/api/v1/sys/notice/unread-count")).ReadEnvelope())
            .GetProperty("data").GetInt32());

        // 我的:isRead=true
        var mine2 = await (await c.GetAsync("/api/v1/sys/notice/mine?Current=1&Size=10")).ReadEnvelope();
        Assert.True(mine2.GetProperty("data").GetProperty("items").EnumerateArray().First()
            .GetProperty("isRead").GetBoolean());

        // 删除后列表清空
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/notice/{id}")).ReadEnvelope())
            .GetProperty("code").GetInt32());
        var page = await (await c.GetAsync("/api/v1/sys/notice/page?Current=1&Size=50")).ReadEnvelope();
        Assert.Empty(page.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Mark_all_read_clears_unread()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        await c.PostJson("/api/v1/sys/notice", new { title = "通知A", type = 1 });
        await c.PostJson("/api/v1/sys/notice", new { title = "通知B", type = 1 });
        Assert.Equal(2, (await (await c.GetAsync("/api/v1/sys/notice/unread-count")).ReadEnvelope())
            .GetProperty("data").GetInt32());

        Assert.Equal(0, (await (await c.PutJson("/api/v1/sys/notice/read-all", new { })).ReadEnvelope())
            .GetProperty("code").GetInt32());
        Assert.Equal(0, (await (await c.GetAsync("/api/v1/sys/notice/unread-count")).ReadEnvelope())
            .GetProperty("data").GetInt32());
    }

    /// <summary>
    /// 定向发送:发给指定用户 / 指定角色的通知只对目标可见;全体广播(receiverType 缺省=All)人人可见。
    /// 证明 mine/unread 已按接收范围收敛,且发布者(超管)不因发布就自动可见非定向消息。
    /// </summary>
    [Fact]
    public async Task Targeted_notice_visible_only_to_recipients()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var vip = await AddRole(admin, "vip");
        var targetId = await AddUser(admin, "u-target", vip);
        await AddUser(admin, "u-other");

        await admin.PostJson("/api/v1/sys/notice", new { title = "给你", type = 1, receiverType = 2, receiverIds = new[] { targetId } });
        await admin.PostJson("/api/v1/sys/notice", new { title = "给VIP", type = 1, receiverType = 1, receiverIds = new[] { vip } });
        await admin.PostJson("/api/v1/sys/notice", new { title = "全体", type = 2 }); // receiverType 缺省 = All

        // 目标用户:定向给我的 + vip 角色的 + 全体,共 3 条
        var target = await ClientFor(f, "u-target");
        var targetTitles = await MineTitles(target);
        Assert.Equal(3, targetTitles.Count);
        Assert.Contains("给你", targetTitles);
        Assert.Contains("给VIP", targetTitles);
        Assert.Contains("全体", targetTitles);
        Assert.Equal(3, await Unread(target));

        // 无关用户:只看到全体广播
        var other = await ClientFor(f, "u-other");
        Assert.Equal(["全体"], await MineTitles(other));
        Assert.Equal(1, await Unread(other));

        // 发布者(超管)不持 vip、非定向对象 → 也只看到全体广播
        Assert.Equal(["全体"], await MineTitles(admin));
        Assert.Equal(1, await Unread(admin));
    }

    /// <summary>OnlyUnread=true 只返回未读(读掉一条后它从"未读"页签消失,"全部"仍在)。</summary>
    [Fact]
    public async Task OnlyUnread_filters_out_read_items()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var a = (await (await c.PostJson("/api/v1/sys/notice", new { title = "A", type = 1 })).ReadEnvelope())
            .GetProperty("data").GetInt64();
        await c.PostJson("/api/v1/sys/notice", new { title = "B", type = 1 });

        await c.PutJson($"/api/v1/sys/notice/{a}/read", new { }); // 读掉 A

        Assert.Equal(2, (await MineTitles(c)).Count);                                   // 全部:A、B
        Assert.Equal(["B"], await MineTitles(c, "Current=1&Size=50&OnlyUnread=true")); // 仅未读:只剩 B
    }
}
