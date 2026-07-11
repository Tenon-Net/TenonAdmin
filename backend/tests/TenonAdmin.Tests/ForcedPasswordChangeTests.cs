using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 首登强制改密(设计 §14)HTTP 级回归:管理员建号 → 首登 mustChangePassword=true → 自助改密清标志;
/// 管理员重置密码 → 标志再次置 true。后端不拦登录,仅经 LoginOutput 透传标志。
/// </summary>
public class ForcedPasswordChangeTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<bool> LoginMustChange(HttpClient anon, string account, string password)
    {
        var login = await (await anon.PostJson("/api/v1/auth/login", new { account, password })).ReadEnvelope();
        return login.GetProperty("data").GetProperty("mustChangePassword").GetBoolean();
    }

    [Fact]
    public async Task Created_user_must_change_then_flag_clears_after_self_change()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var anon = f.CreateClient();

        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "alice", password = "InitPass123", name = "Alice", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());

        // 首登:强制改密标志为 true(登录未被拦,拿得到令牌)
        var login1 = await (await anon.PostJson("/api/v1/auth/login", new { account = "alice", password = "InitPass123" })).ReadEnvelope();
        Assert.True(login1.GetProperty("data").GetProperty("mustChangePassword").GetBoolean());
        var token = login1.GetProperty("data").GetProperty("accessToken").GetString()!;

        // 自助改密 → 清标志
        var alice = f.CreateClient();
        alice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(0, (await (await alice.PutJson("/api/v1/personal/password",
            new { oldPassword = "InitPass123", newPassword = "NewPass456" })).ReadEnvelope()).GetProperty("code").GetInt32());

        // 再登:标志已清
        Assert.False(await LoginMustChange(anon, "alice", "NewPass456"));
    }

    [Fact]
    public async Task Admin_reset_sets_must_change_again()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var anon = f.CreateClient();

        // 建号 → 首登改密清标志
        await admin.PostJson("/api/v1/sys/user",
            new { account = "bob", password = "InitPass123", name = "Bob", enabled = true, roleIds = Array.Empty<long>() });
        var token = (await (await anon.PostJson("/api/v1/auth/login", new { account = "bob", password = "InitPass123" })).ReadEnvelope())
            .GetProperty("data").GetProperty("accessToken").GetString()!;
        var bob = f.CreateClient();
        bob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await bob.PutJson("/api/v1/personal/password", new { oldPassword = "InitPass123", newPassword = "NewPass456" });
        Assert.False(await LoginMustChange(anon, "bob", "NewPass456"));

        // 管理员重置密码 → 标志再次置 true
        var page = await (await admin.GetAsync("/api/v1/sys/user/page?Current=1&Size=50&Account=bob")).ReadEnvelope();
        var bobId = page.GetProperty("data").GetProperty("items").EnumerateArray().First().GetProperty("id").GetInt64();
        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/sys/user/{bobId}/password",
            new { newPassword = "ResetPass789" })).ReadEnvelope()).GetProperty("code").GetInt32());

        Assert.True(await LoginMustChange(anon, "bob", "ResetPass789"));
    }
}
