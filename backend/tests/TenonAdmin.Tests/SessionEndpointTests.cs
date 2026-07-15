using System.Net;
using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 会话管理端点的 HTTP 级覆盖(设计 §15):在线列表 + 强退。
/// <see cref="SessionConcurrencyTests"/> 测的是并发收敛语义,这里测端点本身的可观察行为
/// (谁能看见谁在线、强退后原令牌即时 401、踢一个不存在的 sessionId 不炸)。
/// </summary>
public class SessionEndpointTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Online_list_contains_the_just_logged_in_super_admin_session()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var j = await (await admin.GetAsync("/api/v1/sys/session/online?Current=1&Size=100")).ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());

        var items = j.GetProperty("data").GetProperty("items");
        Assert.Contains(items.EnumerateArray(),
            item => item.GetProperty("account").GetString() == "superAdmin");
    }

    [Fact]
    public async Task Force_logout_by_session_id_401s_the_kicked_client_immediately()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 造一个普通用户,单独登录、单独持有令牌 —— 别踢到自己手里用来调用强退接口的 admin 客户端
        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "kickme", password = "Test@123456", name = "Kick Me", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());

        var victim = f.CreateClient();
        victim.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await victim.LoginToken("kickme", "Test@123456"));

        // 强退前:被踢客户端能正常访问受保护端点
        Assert.Equal(HttpStatusCode.OK, (await victim.GetAsync("/api/v1/personal/profile")).StatusCode);

        // 从在线列表里找出这个会话的 sessionId
        var online = await (await admin.GetAsync("/api/v1/sys/session/online?Current=1&Size=100")).ReadEnvelope();
        var items = online.GetProperty("data").GetProperty("items");
        var sessionId = items.EnumerateArray()
            .First(item => item.GetProperty("account").GetString() == "kickme")
            .GetProperty("sessionId").GetString();

        var kick = await (await admin.DeleteAsync($"/api/v1/sys/session/{sessionId}")).ReadEnvelope();
        Assert.Equal(0, kick.GetProperty("code").GetInt32());

        // 强退后:被踢客户端下一次带旧令牌的请求立即 401(不等令牌自然过期)
        var after = await victim.GetAsync("/api/v1/personal/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Force_logout_of_a_nonexistent_session_id_is_idempotent_ok()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // SessionService.RevokeAsync 是条件 UPDATE(按 sessionId 匹配),不存在的 id 只是 0 行受影响,
        // 不查存在性、不抛异常 —— 控制器永远回 Result<bool>.Ok(true)。
        var response = await admin.DeleteAsync("/api/v1/sys/session/does-not-exist");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var j = await response.ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        Assert.True(j.GetProperty("data").GetBoolean());
    }
}
