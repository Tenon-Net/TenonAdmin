using System.Net;
using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 管理员重置密码的两条安全语义(HTTP 级):重置 = "不再信任该账号现有持有者"。
/// <para>① 旧会话必须立即失效——否则盗号者手里的 access/refresh 纹丝不动
/// (refresh 不校验密码版本且滑动续期 → 可无限续命),重置对攻击者实际影响为 0;</para>
/// <para>② 登录失败锁定必须一并解除——锁定判定在账密校验之前,不清计数则"管理员重置了密码但用户仍登不上"。</para>
/// </summary>
public class PasswordResetSecurityTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<long> AddUser(HttpClient admin, string account, string password)
    {
        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account, password, name = account, enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        return add.GetProperty("data").GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task Reset_password_revokes_existing_sessions()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var anon = f.CreateClient();

        var id = await AddUser(admin, "carol", "InitPass123");

        // carol 登录并拿到一对令牌;此时 access 令牌可用(personal/profile 是 [ActiveSession] 端点)
        var login = await (await anon.PostJson("/api/v1/auth/login",
            new { account = "carol", password = "InitPass123" })).ReadEnvelope();
        var accessToken = login.GetProperty("data").GetProperty("accessToken").GetString()!;
        var refreshToken = login.GetProperty("data").GetProperty("refreshToken").GetString()!;

        var carol = f.CreateClient();
        carol.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await carol.GetAsync("/api/v1/personal/profile")).StatusCode);

        // 管理员重置密码
        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/sys/user/{id}/password",
            new { newPassword = "ResetPass789" })).ReadEnvelope()).GetProperty("code").GetInt32());

        // 旧 access 令牌立即失效(会话被吊销 → 过滤器判会话不活跃)
        Assert.Equal(HttpStatusCode.Unauthorized, (await carol.GetAsync("/api/v1/personal/profile")).StatusCode);

        // 旧 refresh 令牌也换不出新令牌(否则攻击者可持续续命)
        Assert.NotEqual(0, (await (await anon.PostJson("/api/v1/auth/refresh", new { refreshToken })).ReadEnvelope())
            .GetProperty("code").GetInt32());

        // 新密码可正常登录(重置本身没把账号弄坏)
        Assert.Equal(0, (await (await anon.PostJson("/api/v1/auth/login",
            new { account = "carol", password = "ResetPass789" })).ReadEnvelope()).GetProperty("code").GetInt32());
    }

    /// <summary>
    /// A2:留空口令建号必须把系统生成的随机口令回传给管理员——否则这个号谁也登不进去,建出来即死号。
    /// </summary>
    [Fact]
    public async Task Add_user_without_password_returns_usable_initial_password()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var anon = f.CreateClient();

        // 不给 password:走 Security:DefaultInitialPassword(默认未配)→ 随机强口令
        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "frank", name = "Frank", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());

        var initial = add.GetProperty("data").GetProperty("initialPassword").GetString();
        Assert.False(string.IsNullOrEmpty(initial));

        // 回传的这个口令必须真能登进去(而不是一个"看起来像密码"的串)
        var login = await (await anon.PostJson("/api/v1/auth/login",
            new { account = "frank", password = initial })).ReadEnvelope();
        Assert.Equal(0, login.GetProperty("code").GetInt32());
        Assert.True(login.GetProperty("data").GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Reset_password_clears_login_lock()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var anon = f.CreateClient();

        var id = await AddUser(admin, "dave", "InitPass123");

        // 连续 5 次密码错误 → 账号锁定(默认 MaxFailCount=5),此时正确口令也进不来
        for (var i = 0; i < 5; i++)
            await anon.PostJson("/api/v1/auth/login", new { account = "dave", password = "wrong" });
        Assert.Equal(40004, (await (await anon.PostJson("/api/v1/auth/login",
            new { account = "dave", password = "InitPass123" })).ReadEnvelope()).GetProperty("code").GetInt32());

        // 管理员重置密码 → 连带解锁,用户立刻可用新口令登入(不必干等锁定窗口过期)
        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/sys/user/{id}/password",
            new { newPassword = "ResetPass789" })).ReadEnvelope()).GetProperty("code").GetInt32());

        Assert.Equal(0, (await (await anon.PostJson("/api/v1/auth/login",
            new { account = "dave", password = "ResetPass789" })).ReadEnvelope()).GetProperty("code").GetInt32());
    }
}
