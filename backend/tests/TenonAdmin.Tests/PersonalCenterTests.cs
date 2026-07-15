using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 个人中心端点(设计 §4,T8)HTTP 级回归:看/改自己的资料,自助改密的正确性(验旧密码)与安全性
/// (新口令须过密码策略;改密成功后旧口令失效、新口令可登),以及权限码集合的可用性。
/// 改密用例专用普通用户建号,不碰共享的 superAdmin 凭据(避免锁死其它测试依赖的口令)。
/// </summary>
public class PersonalCenterTests
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

        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, password));
        return client;
    }

    [Fact]
    public async Task GetProfile_returns_current_user_account()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var erin = await AddUserAndLogin(f, admin, "erin", "InitPass123");

        var profile = await (await erin.GetAsync("/api/v1/personal/profile")).ReadEnvelope();
        Assert.Equal(0, profile.GetProperty("code").GetInt32());
        Assert.Equal("erin", profile.GetProperty("data").GetProperty("account").GetString());
    }

    [Fact]
    public async Task UpdateProfile_changes_name_and_reflects_in_get()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var erin = await AddUserAndLogin(f, admin, "erin", "InitPass123");

        var update = await (await erin.PutJson("/api/v1/personal/profile", new { name = "Erin Updated" })).ReadEnvelope();
        Assert.Equal(0, update.GetProperty("code").GetInt32());

        var profile = await (await erin.GetAsync("/api/v1/personal/profile")).ReadEnvelope();
        Assert.Equal("Erin Updated", profile.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ChangePassword_wrong_old_password_returns_PasswordWrong()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var gina = await AddUserAndLogin(f, admin, "gina", "InitPass123");

        var change = await (await gina.PutJson("/api/v1/personal/password",
            new { oldPassword = "WrongOldPass1", newPassword = "NewStrongPass1" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PasswordWrong, change.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ChangePassword_weak_new_password_returns_PasswordTooWeak()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var heidi = await AddUserAndLogin(f, admin, "heidi", "InitPass123");

        // 新口令仅 3 位,短于默认策略 minLength=8(SecurityPolicyProvider.DEFAULT_MIN_LEN)
        var change = await (await heidi.PutJson("/api/v1/personal/password",
            new { oldPassword = "InitPass123", newPassword = "Ab1" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PasswordTooWeak, change.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ChangePassword_success_then_old_rejected_new_accepted()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var anon = f.CreateClient();
        var ivan = await AddUserAndLogin(f, admin, "ivan", "InitPass123");

        var change = await (await ivan.PutJson("/api/v1/personal/password",
            new { oldPassword = "InitPass123", newPassword = "NewStrongPass1" })).ReadEnvelope();
        Assert.Equal(0, change.GetProperty("code").GetInt32());

        // 旧口令不再可登录
        var oldLogin = await (await anon.PostJson("/api/v1/auth/login", new { account = "ivan", password = "InitPass123" })).ReadEnvelope();
        Assert.NotEqual(0, oldLogin.GetProperty("code").GetInt32());

        // 新口令可正常登录
        var newLogin = await (await anon.PostJson("/api/v1/auth/login", new { account = "ivan", password = "NewStrongPass1" })).ReadEnvelope();
        Assert.Equal(0, newLogin.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task GetPermissions_superAdmin_returns_array()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var permissions = await (await admin.GetAsync("/api/v1/personal/permissions")).ReadEnvelope();
        Assert.Equal(0, permissions.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Array, permissions.GetProperty("data").ValueKind);
    }
}
