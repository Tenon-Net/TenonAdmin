using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 内置字典的鉴权与失败契约。增删改成功路径见 <see cref="DictCrudTests"/>。
/// 默认工厂禁用 Dict，管理路由应 404；<c>items/{typeCode}</c> 只要求登录。
/// </summary>
public class DictRobustnessTests
{
    [Fact]
    public async Task Builtin_dict_routes_are_absent_while_module_disabled()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdmin(f);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/sys/dict/type/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/sys/dict/items/common_status")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_rejected_and_logged_in_user_can_read_items_only()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var anon = f.CreateClient();
        // 不用 type/page:TestHost 的 CustomDictController 也映射了该路由,两边同时启用会 500。
        var typeGet = await anon.GetAsync("/api/v1/sys/dict/type/1");
        Assert.Equal(HttpStatusCode.Unauthorized, typeGet.StatusCode);
        Assert.Equal(40006, (await typeGet.ReadEnvelope()).GetProperty("code").GetInt32());

        var items = await anon.GetAsync("/api/v1/sys/dict/items/common_status");
        Assert.Equal(HttpStatusCode.Unauthorized, items.StatusCode);
        Assert.Equal(40006, (await items.ReadEnvelope()).GetProperty("code").GetInt32());

        var limited = await PingOnly(f);
        var denied = await limited.GetAsync("/api/v1/sys/dict/type/1");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(41001, (await denied.ReadEnvelope()).GetProperty("code").GetInt32());

        var allowed = await (await limited.GetAsync("/api/v1/sys/dict/items/common_status")).ReadEnvelope();
        Assert.Equal(0, allowed.GetProperty("code").GetInt32());
        Assert.NotEmpty(allowed.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Duplicate_type_code_and_missing_type_use_dict_error_codes()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdmin(f);

        var missing = await (await admin.GetAsync("/api/v1/sys/dict/type/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.DictTypeNotFound, missing.GetProperty("code").GetInt32());

        var missingItem = await (await admin.DeleteAsync("/api/v1/sys/dict/item/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.DictItemNotFound, missingItem.GetProperty("code").GetInt32());

        var first = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "dup_status", name = "一次", sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        var dup = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "dup_status", name = "再次", sort = 2, enabled = true })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.DictTypeCodeExists, dup.GetProperty("code").GetInt32());

        var seeded = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "common_status", name = "撞种子", sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.DictTypeCodeExists, seeded.GetProperty("code").GetInt32());
    }

    private static async Task<HttpClient> SuperAdmin(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<HttpClient> PingOnly(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
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
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));
        return c;
    }
}
