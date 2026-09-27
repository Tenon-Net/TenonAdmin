using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>这些控制器已有成功路径测试,但未锁未登录 / 无权限。这里只补那一层,外加少量明确的错误码。</summary>
internal static class AuthProbe
{
    public static Task<HttpClient> SuperAdmin(AdminAppFactory f) =>
        SuperAdmin(() => f.CreateClient());

    public static Task<HttpClient> SuperAdmin(WorkflowAppFactory f) =>
        SuperAdmin(() => f.CreateClient());

    private static async Task<HttpClient> SuperAdmin(Func<HttpClient> createClient)
    {
        var c = createClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    public static Task<HttpClient> PingOnly(AdminAppFactory f) =>
        PingOnly(f.Services, () => f.CreateClient());

    public static Task<HttpClient> PingOnly(WorkflowAppFactory f) =>
        PingOnly(f.Services, () => f.CreateClient());

    private static async Task<HttpClient> PingOnly(IServiceProvider services, Func<HttpClient> createClient)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var role = new SysRole
        {
            Name = "受限角色",
            Code = "limited-" + Guid.CreateVersion7().ToString("N")[..8],
            Enabled = true,
        };
        await roles.InsertAsync(role);
        await sp.GetRequiredService<IRbacService>().SetRoleMenusAsync(role.Id, [2]);
        var account = "limited-" + Guid.CreateVersion7().ToString("N")[..8];
        const string password = "Limited@123456";
        await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = account,
            Password = password,
            Name = "受限用户",
            Enabled = true,
            RoleIds = [role.Id],
        });
        var c = createClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));
        return c;
    }

    public static async Task AssertUnauthorized(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(40006, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    public static async Task AssertForbidden(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(41001, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }
}

public class ExternalAuthRobustnessTests
{
    [Fact]
    public async Task Admin_provider_list_requires_permission_bindings_require_login()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/auth/external/providers/all"));
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/auth/external/bindings"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/auth/external/providers/all"));
        var bindings = await (await limited.GetAsync("/api/v1/auth/external/bindings")).ReadEnvelope();
        Assert.Equal(0, bindings.GetProperty("code").GetInt32());
    }
}

public class JobAuthTests
{
    [Fact]
    public async Task Page_requires_permission_preview_cron_requires_login()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/job/page"));
        await AuthProbe.AssertUnauthorized(await anon.PostJson("/api/v1/sys/job/preview-cron",
            new { cron = "0 0 4 * * ?", count = 1 }));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/job/page"));
        var preview = await (await limited.PostJson("/api/v1/sys/job/preview-cron",
            new { cron = "0 0 4 * * ?", count = 1 })).ReadEnvelope();
        Assert.Equal(0, preview.GetProperty("code").GetInt32());
    }
}

public class MenuAuthTests
{
    [Fact]
    public async Task Tree_and_routes_require_menu_permission()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/menu/tree"));
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/menu/routes"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/menu/tree"));
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/menu/routes"));
    }
}

public class MfaAdminAuthTests
{
    [Fact]
    public async Task Admin_mfa_routes_require_permission_reauth_requires_login()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/mfa/high-sensitivity"));
        await AuthProbe.AssertUnauthorized(await anon.PostJson("/api/v1/sys/mfa/clear", new { userId = 1 }));
        await AuthProbe.AssertUnauthorized(await anon.PostJson("/api/v1/auth/reauth", new { password = "x" }));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/mfa/high-sensitivity"));
        await AuthProbe.AssertForbidden(await limited.PostJson("/api/v1/sys/mfa/clear", new { userId = 1 }));
    }
}

public class ModuleAuthTests
{
    [Fact]
    public async Task List_requires_permission_and_missing_id_is_42011()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/module/list"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/module/list"));

        var admin = await AuthProbe.SuperAdmin(f);
        var missing = await (await admin.GetAsync("/api/v1/sys/module/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ModuleNotFound, missing.GetProperty("code").GetInt32());
    }
}

public class NoticeAuthTests
{
    [Fact]
    public async Task Admin_page_requires_permission_mine_requires_login()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/notice/page"));
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/notice/unread-count"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/notice/page"));
        var unread = await (await limited.GetAsync("/api/v1/sys/notice/unread-count")).ReadEnvelope();
        Assert.Equal(0, unread.GetProperty("code").GetInt32());

        var admin = await AuthProbe.SuperAdmin(f);
        var missing = await (await admin.DeleteAsync("/api/v1/sys/notice/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.NoticeNotFound, missing.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Disabled_module_hides_notice_routes()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Notice"] };
        var admin = await AuthProbe.SuperAdmin(f);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/sys/notice/page")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/sys/notice/unread-count")).StatusCode);
    }
}

public class OrgAuthTests
{
    [Fact]
    public async Task List_requires_permission_and_copy_missing_org_is_42003()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/org/list"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/org/list"));

        var admin = await AuthProbe.SuperAdmin(f);
        var copy = await (await admin.PostJson("/api/v1/sys/org/999999999/copy", new { name = "不存在" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.OrgNotFound, copy.GetProperty("code").GetInt32());
    }
}

public class PersonalAuthTests
{
    [Fact]
    public async Task Profile_requires_login_and_unknown_session_is_42024()
    {
        using var f = new AdminAppFactory();
        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/personal/profile"));

        var limited = await AuthProbe.PingOnly(f);
        var profile = await (await limited.GetAsync("/api/v1/personal/profile")).ReadEnvelope();
        Assert.Equal(0, profile.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(profile.GetProperty("data").GetProperty("account").GetString()));

        var kick = await (await limited.DeleteAsync("/api/v1/personal/sessions/does-not-exist")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.SessionNotFound, kick.GetProperty("code").GetInt32());
    }
}

public class PositionAuthTests
{
    [Fact]
    public async Task Page_requires_permission_missing_id_is_42005_and_held_position_cannot_delete()
    {
        using var f = new AdminAppFactory();
        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/sys/position/page"));
        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/position/page"));

        var admin = await AuthProbe.SuperAdmin(f);
        var missing = await (await admin.PutJson("/api/v1/sys/position/999999999",
            new { name = "无", code = "NONE", sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PositionNotFound, missing.GetProperty("code").GetInt32());
        var deleteMissing = await (await admin.DeleteAsync("/api/v1/sys/position/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PositionNotFound, deleteMissing.GetProperty("code").GetInt32());

        var code = "HOLD_" + Guid.NewGuid().ToString("N")[..8];
        var add = await (await admin.PostJson("/api/v1/sys/position/add",
            new { name = "在用职位", code, sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var id = add.GetProperty("data").GetInt64();

        using (var scope = f.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = "holder-" + Guid.NewGuid().ToString("N")[..8],
                Password = "Limited@123456",
                Name = "占位",
                Enabled = true,
                PositionId = id,
                RoleIds = [],
            });
        }

        var blocked = await (await admin.DeleteAsync($"/api/v1/sys/position/{id}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PositionHasUsers, blocked.GetProperty("code").GetInt32());
        var still = await (await admin.GetAsync($"/api/v1/sys/position/{id}")).ReadEnvelope();
        Assert.Equal(0, still.GetProperty("code").GetInt32());
        Assert.Equal(code, still.GetProperty("data").GetProperty("code").GetString());
    }
}
