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
}
