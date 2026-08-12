using System.Net;
using System.Net.Http.Headers;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 个人中心"我的会话"端点(GET/DELETE /personal/sessions)与个人资料扩展字段的 HTTP 级回归:
/// 列表只见自己的会话且恰一行 IsCurrent;踢他人会话按"不存在"拒(42024,防探测);
/// 踢自己另一会话后其令牌立即 401(与管理端强退同语义)。
/// </summary>
public class PersonalSessionTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>以超管建一个普通用户并登录,返回带其令牌的客户端。</summary>
    private static async Task<HttpClient> AddUserAndLogin(AdminAppFactory f, HttpClient admin, string account, string password)
    {
        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account, password, name = account, enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        return await Login(f, account, password);
    }

    private static async Task<HttpClient> Login(AdminAppFactory f, string account, string password)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, password));
        return client;
    }

    [Fact]
    public async Task UpdateProfile_updates_contact_fields_and_reflects_in_get()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var noah = await AddUserAndLogin(f, admin, "noah", "InitPass123");

        var update = await (await noah.PutJson("/api/v1/personal/profile", new
        {
            name = "Noah",
            nickname = "小诺",
            phone = "13800000000",
            email = "noah@example.com",
            gender = "1",
            avatar = (string?)null,
        })).ReadEnvelope();
        Assert.Equal(0, update.GetProperty("code").GetInt32());

        var data = (await (await noah.GetAsync("/api/v1/personal/profile")).ReadEnvelope()).GetProperty("data");
        Assert.Equal("小诺", data.GetProperty("nickname").GetString());
        Assert.Equal("13800000000", data.GetProperty("phone").GetString());
        Assert.Equal("noah@example.com", data.GetProperty("email").GetString());
        Assert.Equal("1", data.GetProperty("gender").GetString());
    }

    [Fact]
    public async Task GetSessions_lists_own_and_marks_current()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var olive1 = await AddUserAndLogin(f, admin, "olive", "InitPass123");
        var olive2 = await Login(f, "olive", "InitPass123");   // 同账号第二端(默认多端并存,不互踢)
        var pete = await AddUserAndLogin(f, admin, "pete", "InitPass123");

        var mine = (await (await olive1.GetAsync("/api/v1/personal/sessions")).ReadEnvelope()).GetProperty("data");
        var items = mine.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Single(items, i => i.GetProperty("isCurrent").GetBoolean());

        // 另一端视角:同两条会话,但 IsCurrent 落在另一行
        var mine2 = (await (await olive2.GetAsync("/api/v1/personal/sessions")).ReadEnvelope()).GetProperty("data");
        var current1 = items.Single(i => i.GetProperty("isCurrent").GetBoolean()).GetProperty("sessionId").GetString();
        var current2 = mine2.EnumerateArray().Single(i => i.GetProperty("isCurrent").GetBoolean()).GetProperty("sessionId").GetString();
        Assert.NotEqual(current1, current2);

        // 他人列表不含我的会话(UserId 过滤)
        var petes = (await (await pete.GetAsync("/api/v1/personal/sessions")).ReadEnvelope()).GetProperty("data");
        var oliveIds = items.Select(i => i.GetProperty("sessionId").GetString()).ToHashSet();
        Assert.DoesNotContain(petes.EnumerateArray(), i => oliveIds.Contains(i.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task RevokeSession_foreign_session_returns_SessionNotFound()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var quinn = await AddUserAndLogin(f, admin, "quinn", "InitPass123");
        var ruth = await AddUserAndLogin(f, admin, "ruth", "InitPass123");

        var ruthSid = (await (await ruth.GetAsync("/api/v1/personal/sessions")).ReadEnvelope())
            .GetProperty("data").EnumerateArray()
            .Single(i => i.GetProperty("isCurrent").GetBoolean()).GetProperty("sessionId").GetString();

        // quinn 拿 ruth 的 sessionId 自助下线:按"不存在"拒,且 ruth 的会话不受影响
        var kick = await (await quinn.DeleteAsync($"/api/v1/personal/sessions/{ruthSid}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.SessionNotFound, kick.GetProperty("code").GetInt32());

        var still = await (await ruth.GetAsync("/api/v1/personal/profile")).ReadEnvelope();
        Assert.Equal(0, still.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task RevokeSession_own_other_session_then_its_token_rejected()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var sam1 = await AddUserAndLogin(f, admin, "sam", "InitPass123");
        var sam2 = await Login(f, "sam", "InitPass123");

        // 端 1 视角找出"另一端"的会话并踢掉
        var otherSid = (await (await sam1.GetAsync("/api/v1/personal/sessions")).ReadEnvelope())
            .GetProperty("data").EnumerateArray()
            .Single(i => !i.GetProperty("isCurrent").GetBoolean()).GetProperty("sessionId").GetString();
        var kick = await (await sam1.DeleteAsync($"/api/v1/personal/sessions/{otherSid}")).ReadEnvelope();
        Assert.Equal(0, kick.GetProperty("code").GetInt32());

        // 被踢端下一次请求立即 401(不等令牌自然过期);踢人端不受影响
        var after = await sam2.GetAsync("/api/v1/personal/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        var self = await (await sam1.GetAsync("/api/v1/personal/profile")).ReadEnvelope();
        Assert.Equal(0, self.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// QA04:自助改密(PUT /personal/password)只吊销"其它"活跃会话,当前会话保留——
    /// 本次改密请求所用会话本身仍可用(直到用户主动登出),而另一端立即 401。
    /// </summary>
    [Fact]
    public async Task ChangePassword_revokes_other_sessions_but_keeps_current()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var tara1 = await AddUserAndLogin(f, admin, "tara", "InitPass123");
        var tara2 = await Login(f, "tara", "InitPass123");   // 同账号第二端

        var change = await (await tara1.PutJson("/api/v1/personal/password", new
        {
            oldPassword = "InitPass123",
            newPassword = "NewPass1234",
        })).ReadEnvelope();
        Assert.Equal(0, change.GetProperty("code").GetInt32());

        // 改密所用的当前会话(tara1)本次响应正常返回,且后续请求仍活跃(直到自愿登出)
        var self = await (await tara1.GetAsync("/api/v1/personal/profile")).ReadEnvelope();
        Assert.Equal(0, self.GetProperty("code").GetInt32());

        // 另一端(tara2)立即 401,不等令牌自然过期
        var other = await tara2.GetAsync("/api/v1/personal/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }
}
