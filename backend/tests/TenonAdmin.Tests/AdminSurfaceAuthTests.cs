using System.Net;
using System.Net.Http.Headers;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 回收站、文件、日志、角色、会话、用户的鉴权与少量失败码。
/// 成功路径分别在既有 CRUD / 回收站 / 文件归属测试里。
/// </summary>
public class RecycleBinAuthTests
{
    [Fact]
    public async Task Page_requires_permission_and_missing_row_is_42020()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/recycle/role/page"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/recycle/role/page"));

        var admin = await AuthProbe.SuperAdmin(f);
        var restore = await (await admin.PostJson("/api/v1/sys/recycle/role/999999999/restore", new { })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.RecycleNotFound, restore.GetProperty("code").GetInt32());
        var purge = await (await admin.DeleteAsync("/api/v1/sys/recycle/position/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.RecycleNotFound, purge.GetProperty("code").GetInt32());
    }
}

public class FileAuthTests
{
    [Fact]
    public async Task Page_requires_permission_and_bad_view_signature_is_403()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/file/page"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/file/page"));

        // 签名在查库之前校验:没有 sig 不能用来探测文件是否存在,且不是统一信封。
        var view = await anon.GetAsync("/api/v1/sys/file/1/view");
        Assert.Equal(HttpStatusCode.Forbidden, view.StatusCode);
    }
}

public class LogAuthTests
{
    [Fact]
    public async Task Pages_require_permission_and_module_can_be_disabled()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/log/op/page"));
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/log/login/page"));
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/log/exception/page"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/log/op/page"));

        var admin = await AuthProbe.SuperAdmin(f);
        var cleared = await (await admin.DeleteAsync("/api/v1/sys/log/login")).ReadEnvelope();
        Assert.Equal(0, cleared.GetProperty("code").GetInt32());
        var loginPage = await (await admin.GetAsync("/api/v1/sys/log/login/page?Current=1&Size=20")).ReadEnvelope();
        Assert.Equal(0, loginPage.GetProperty("code").GetInt32());
        Assert.Empty(loginPage.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Disabled_module_hides_log_routes()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Log"] };
        var admin = await AuthProbe.SuperAdmin(f);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/sys/log/op/page")).StatusCode);
    }
}

public class RoleAuthTests
{
    [Fact]
    public async Task Page_requires_permission_and_missing_role_is_42002()
    {
        using var f = new AdminAppFactory();
        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/sys/role/page"));
        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/role/page"));

        var admin = await AuthProbe.SuperAdmin(f);
        var missing = await (await admin.GetAsync("/api/v1/sys/role/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.RoleNotFound, missing.GetProperty("code").GetInt32());
    }
}

public class SessionAuthTests
{
    [Fact]
    public async Task Online_list_requires_session_admin_permission()
    {
        using var f = new AdminAppFactory();
        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/sys/session/online"));
        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/session/online"));
    }
}

public class UserAuthTests
{
    [Fact]
    public async Task Page_requires_permission_template_requires_login_missing_user_is_42001()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/user/page"));
        await AuthProbe.AssertUnauthorized(await anon.GetAsync("/api/v1/sys/user/import/template"));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/sys/user/page"));
        // 测试宿主未装 Excel，登录用户会走到 46001，而不是 403。这正好证明 [ActiveSession] 已放行。
        var template = await (await limited.GetAsync("/api/v1/sys/user/import/template")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ExcelProviderMissing, template.GetProperty("code").GetInt32());

        var admin = await AuthProbe.SuperAdmin(f);
        var missing = await (await admin.GetAsync("/api/v1/sys/user/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.UserNotFound, missing.GetProperty("code").GetInt32());
    }
}
