using System.Net.Http.Headers;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 密码历史防重用(B3)——开关 <c>sys.security.password.historyCount</c> 默认 0=关(可重用);
/// 开启后改密的新口令不得与当前或最近 N 个用过的口令相同(<see cref="ErrorCode.PasswordReused"/>)。
/// </summary>
public class PasswordHistoryTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task EnableHistory(HttpClient admin, int n) =>
        (await admin.PutJson("/api/v1/sys/config/batch",
            new[] { new { configKey = "sys.security.password.historyCount", configValue = n.ToString() } }))
            .EnsureSuccessStatusCode();

    private static async Task<HttpClient> LoginAs(AdminAppFactory f, string account, string password)
    {
        var token = (await (await f.CreateClient().PostJson("/api/v1/auth/login",
            new { account, password })).ReadEnvelope()).GetProperty("data").GetProperty("accessToken").GetString()!;
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<System.Text.Json.JsonElement> ChangePassword(HttpClient c, string oldPw, string newPw) =>
        await (await c.PutJson("/api/v1/personal/password", new { oldPassword = oldPw, newPassword = newPw })).ReadEnvelope();

    [Fact]
    public async Task Reuse_current_or_recent_password_is_blocked_when_enabled()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        await EnableHistory(admin, 2);

        // 启用后建号 → AddAsync 记录初始口令
        await admin.PostJson("/api/v1/sys/user",
            new { account = "pwhist", password = "InitPass123", name = "历史测试", enabled = true, roleIds = Array.Empty<long>() });
        var pw = await LoginAs(f, "pwhist", "InitPass123");

        // (a) 改成当前口令 → 被拦
        Assert.Equal((int)ErrorCode.PasswordReused,
            (await ChangePassword(pw, "InitPass123", "InitPass123")).GetProperty("code").GetInt32());

        // (b) 改成全新口令 → 成功
        Assert.Equal(0, (await ChangePassword(pw, "InitPass123", "NewPass111")).GetProperty("code").GetInt32());

        // (c) 改回原口令(仍在最近 2 条内)→ 被拦
        Assert.Equal((int)ErrorCode.PasswordReused,
            (await ChangePassword(pw, "NewPass111", "InitPass123")).GetProperty("code").GetInt32());

        // (d) 再换两个全新口令,把 InitPass123 挤出最近 2 条窗口后 → 可再次使用(证明按 N 裁剪生效)
        Assert.Equal(0, (await ChangePassword(pw, "NewPass111", "NewPass222")).GetProperty("code").GetInt32());
        Assert.Equal(0, (await ChangePassword(pw, "NewPass222", "NewPass333")).GetProperty("code").GetInt32());
        Assert.Equal(0, (await ChangePassword(pw, "NewPass333", "InitPass123")).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Disabled_by_default_allows_reuse()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);   // 不启用(默认 0)

        await admin.PostJson("/api/v1/sys/user",
            new { account = "pwfree", password = "InitPass123", name = "无历史", enabled = true, roleIds = Array.Empty<long>() });
        var pw = await LoginAs(f, "pwfree", "InitPass123");

        // 策略关:改成与当前相同的口令也放行
        Assert.Equal(0, (await ChangePassword(pw, "InitPass123", "InitPass123")).GetProperty("code").GetInt32());
    }
}
