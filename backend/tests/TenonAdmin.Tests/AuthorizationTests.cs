using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 授权与会话强退的 HTTP 级回归(§6/§15)。补 code-review 指出的两处零覆盖:
/// 已认证普通用户按权限码放行/拒绝(P2-4),以及强退后原令牌即 401+40006(P2-22)。
/// </summary>
public class AuthorizationTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>在宿主内造一个挂了指定权限码菜单的角色 + 普通用户,返回其登录账号/密码。</summary>
    private static async Task<(string account, string password)> SeedUserWithPermission(AdminAppFactory f, long menuId)
    {
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var rbac = sp.GetRequiredService<IRbacService>();
        var users = sp.GetRequiredService<IUserService>();

        var role = new SysRole { Name = "受限角色", Code = "limited-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
        await roles.InsertAsync(role);
        await rbac.SetRoleMenusAsync(role.Id, [menuId]);   // 授予该菜单对应的权限码

        var account = "limited-" + Guid.CreateVersion7().ToString("N")[..8];
        const string password = "Limited@123456";
        await users.AddAsync(new AddUserInput { Account = account, Password = password, Name = "受限用户", Enabled = true, RoleIds = [role.Id] });
        return (account, password);
    }

    [Fact]
    public async Task Authenticated_user_allowed_on_granted_code_denied_otherwise()
    {
        // 授予菜单 Id 2 = "GET:/api/v1/ping"(见 DefaultMenuSeed)
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUserWithPermission(f, menuId: 2);

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        // 授了码的接口 → 200 信封
        var ping = await c.GetAsync("/api/v1/ping");
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        Assert.Equal(0, (await ping.ReadEnvelope()).GetProperty("code").GetInt32());

        // 未授码的接口 → 403 + 41001
        var users = await c.GetAsync("/api/v1/sys/user/page?current=1&size=10");
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        Assert.Equal(41001, (await users.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Force_logout_invalidates_token_immediately_with_40006()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        var token = await c.LoginToken("superAdmin", "Test@123456");
        WithToken(c, token);

        // 强退当前会话:超管登出自吊销(sid 取自令牌)
        Assert.Equal(HttpStatusCode.OK, (await c.PostJson("/api/v1/auth/logout", new { })).StatusCode);

        // 原令牌再访问受保护接口 → 401 + 40006(会话失活即时生效,不等令牌自然过期)
        var after = await c.GetAsync("/api/v1/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(40006, (await after.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Personal_endpoint_401s_after_session_revoked()
    {
        // P2-1:个人中心挂 [ActiveSession],强退后原令牌不再可用(此前 [Authorize] 会放行到令牌过期)
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/personal/profile")).StatusCode);
        await c.PostJson("/api/v1/auth/logout", new { });
        var after = await c.GetAsync("/api/v1/personal/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(40006, (await after.ReadEnvelope()).GetProperty("code").GetInt32());
    }
}
