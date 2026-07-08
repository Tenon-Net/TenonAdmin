using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 多应用门户的 HTTP 级回归:模块访问权由菜单授权反推、超管见全部、默认应用设/拒,
/// 以及"加模块不扰动权限码路径"的不变量守护。
/// </summary>
public class ModulePortalTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>造一个授了指定菜单的角色 + 普通用户;menuId=null 则角色不授任何菜单(用于"无授权")。</summary>
    private static async Task<(string account, string password)> SeedUser(AdminAppFactory f, long? menuId)
    {
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var rbac = sp.GetRequiredService<IRbacService>();
        var users = sp.GetRequiredService<IUserService>();

        var role = new SysRole { Name = "门户测试角色", Code = "portal-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
        await roles.InsertAsync(role);
        if (menuId is not null)
            await rbac.SetRoleMenusAsync(role.Id, [menuId.Value]);

        var account = "portal-" + Guid.CreateVersion7().ToString("N")[..8];
        const string password = "Portal@123456";
        await users.AddAsync(new AddUserInput { Account = account, Password = password, Name = "门户用户", Enabled = true, RoleIds = [role.Id] });
        return (account, password);
    }

    private static IEnumerable<long> ModuleIds(System.Text.Json.JsonElement modulesEnvelope) =>
        modulesEnvelope.GetProperty("data").GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("id").GetInt64());

    [Fact]
    public async Task Module_access_is_derived_from_menu_grants()
    {
        // 授菜单 Id 2 = "GET:/api/v1/ping",挂在顶级目录 1(系统管理)下 → 属内置 system 模块(Id 1)
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: 2);

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        var mods = await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope();
        var ids = ModuleIds(mods).ToList();
        Assert.Equal([1L], ids);   // 只可见 system,别无其他

        // 不变量:加了模块特性后,被授的权限码路径照常放行(RbacPermissionProvider 保持模块无关)
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/ping")).StatusCode);
    }

    [Fact]
    public async Task Disabled_menu_grant_does_not_expose_module()
    {
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: 2);

        // 模拟管理员停用已授权菜单。RBAC 权限提供者已按 Enabled 过滤,门户模块反推也必须同口径,
        // 否则用户会看到一个实际无任何生效权限的应用入口。
        using (var scope = f.Services.CreateScope())
        {
            var menus = scope.ServiceProvider.GetRequiredService<IRepository<SysMenu>>();
            var ping = await menus.GetByIdAsync(2);
            Assert.NotNull(ping);
            ping!.Enabled = false;
            await menus.UpdateAsync(ping);
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        var mods = await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope();
        Assert.Empty(ModuleIds(mods));

        // 权限热路径同样不授出已停用菜单权限码。
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/ping")).StatusCode);
    }

    [Fact]
    public async Task No_grants_means_no_modules()
    {
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: null);   // 角色无任何菜单

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        var mods = await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope();
        Assert.Empty(ModuleIds(mods));
    }

    [Fact]
    public async Task SuperAdmin_sees_all_enabled_modules()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));

        // 超管再建一个模块;超管应见 system + 新模块(不依赖授权)
        var newId = (await (await c.PostJson("/api/v1/sys/module/add", new { code = "crm", title = "客户管理", sort = 2, enabled = true })).ReadEnvelope())
            .GetProperty("data").GetInt64();

        var ids = ModuleIds(await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope()).ToList();
        Assert.Contains(1L, ids);
        Assert.Contains(newId, ids);
    }

    [Fact]
    public async Task Menu_tree_scoped_by_module_superadmin_gets_all_catalogs()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));

        // 内置 system 模块下有 4 个顶级目录(1/10/20/30);按钮不入导航,故为 4 个根节点
        var tree = (await (await c.GetAsync("/api/v1/personal/menu?moduleId=1")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(4, tree.GetArrayLength());

        // 不存在/无节点的模块 → 空树
        var empty = (await (await c.GetAsync("/api/v1/personal/menu?moduleId=999999")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(0, empty.GetArrayLength());
    }

    [Fact]
    public async Task Set_default_module_succeeds_for_accessible_and_rejects_inaccessible()
    {
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: 2);   // 可访问 system(Id 1)

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        // 设为可访问的 system → 成功,并被 /modules 回显
        Assert.Equal(0, (await (await c.PutJson("/api/v1/personal/default-module", new { moduleId = 1 })).ReadEnvelope()).GetProperty("code").GetInt32());
        var mods = await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope();
        Assert.Equal(1L, mods.GetProperty("data").GetProperty("defaultModuleId").GetInt64());

        // 设为无权访问的模块 → 42014 ModuleAccessDenied
        var denied = await c.PutJson("/api/v1/personal/default-module", new { moduleId = 999999 });
        Assert.Equal(42014, (await denied.ReadEnvelope()).GetProperty("code").GetInt32());
    }
}
