namespace TenonAdmin.Tests;

/// <summary>认证全流程集成(§8 覆盖点 1):登录 / 错误密码 / 刷新 / 失败锁定。经真实 HTTP 管道。</summary>
public class AuthFlowTests
{
    [Fact]
    public async Task Login_succeeds_and_returns_token()
    {
        using var f = new AdminAppFactory();
        var j = await (await f.CreateClient().PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("data").GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Login_wrong_password_returns_40001()
    {
        using var f = new AdminAppFactory();
        var j = await (await f.CreateClient().PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "nope" })).ReadEnvelope();
        Assert.Equal(40001, j.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Refresh_issues_new_token()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        var login = await (await c.PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        var refreshToken = login.GetProperty("data").GetProperty("refreshToken").GetString();

        var j = await (await c.PostJson("/api/v1/auth/refresh", new { refreshToken })).ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("data").GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Locks_account_after_five_failures()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        for (var i = 0; i < 5; i++)
            await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "wrong" });

        // 第 6 次即使密码正确也被锁 → 40004
        var j = await (await c.PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal(40004, j.GetProperty("code").GetInt32());
    }
}
