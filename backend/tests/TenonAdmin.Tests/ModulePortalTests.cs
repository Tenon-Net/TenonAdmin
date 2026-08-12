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
        // 授菜单 Id 2 = "GET:/api/v1/ping",挂在顶级目录 20(系统运维)下 → 属内置 system 模块(Id 1)
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
    public async Task Disabled_role_does_not_expose_module()
    {
        // 权限码路径已按 Enabled 角色过滤;门户模块反推原先漏了 → 停用角色后 ping 403,侧栏仍见 system。
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: 2);

        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var uid = (await sp.GetRequiredService<IRepository<SysUser>>().GetFirstAsync(u => u.Account == account))!.Id;
            var roleId = (await sp.GetRequiredService<IRbacService>().GetUserRoleIdsAsync(uid)).First();
            var roles = sp.GetRequiredService<IRoleService>();
            var role = await roles.GetAsync(roleId);
            await roles.UpdateAsync(roleId, new RoleInput
            {
                Name = role.Name, Code = role.Code, Sort = role.Sort, Enabled = false, Remark = role.Remark,
            });
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        var mods = await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope();
        Assert.Empty(ModuleIds(mods));
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

        // 内置 system 模块下:工作台(108,根级页面)+ 5 个顶级目录(10/20/147 任务调度/90/30);按钮不入导航,故为 6 个根节点
        var tree = (await (await c.GetAsync("/api/v1/personal/menu?moduleId=1")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(6, tree.GetArrayLength());

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

    // ── 门户读缓存:结果按 (userId[,moduleId]) 缓存,菜单/模块/授权变更经代际计数即时失效(非等 TTL) ──

    [Fact]
    public async Task Portal_modules_cache_reflects_module_add_immediately()
    {
        // 守 ModuleService.AddAsync 的门户代际自增:预热模块列表后新增模块,应即时可见(非陈旧缓存)。
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));

        Assert.Contains(1L, ModuleIds(await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope()));   // 预热(含 system)

        var newId = (await (await c.PostJson("/api/v1/sys/module/add", new { code = "crm2", title = "客户", sort = 5, enabled = true })).ReadEnvelope())
            .GetProperty("data").GetInt64();

        Assert.Contains(newId, ModuleIds(await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope()));   // 即时含新模块
    }

    [Fact]
    public async Task Portal_modules_cache_reflects_role_menu_grant_immediately()
    {
        // 守 RbacService.SetRoleMenusAsync 的门户代际自增:预热空门户后给角色授菜单,模块应即时出现。
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: null);   // 角色无菜单 → 门户空

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));
        Assert.Empty(ModuleIds(await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope()));   // 预热(空)

        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var uid = (await sp.GetRequiredService<IRepository<SysUser>>().GetFirstAsync(u => u.Account == account))!.Id;
            var roleId = (await sp.GetRequiredService<IRbacService>().GetUserRoleIdsAsync(uid)).First();
            await sp.GetRequiredService<IRbacService>().SetRoleMenusAsync(roleId, [2]);   // 授 GET:/api/v1/ping(挂 system 模块下)
        }

        Assert.Equal([1L], ModuleIds(await (await c.GetAsync("/api/v1/personal/modules")).ReadEnvelope()).ToList());   // 即时见 system
    }

    [Fact]
    public async Task Portal_menu_tree_cache_reflects_menu_add_immediately()
    {
        // 守 MenuService.CreateAsync 的门户代际自增:预热某模块菜单树后新增顶级目录,应即时多一个根节点。
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));

        var before = (await (await c.GetAsync("/api/v1/personal/menu?moduleId=1")).ReadEnvelope()).GetProperty("data").GetArrayLength();   // 预热

        await c.PostJson("/api/v1/sys/menu/add",
            new { parentId = 0, type = 1, title = "门户缓存测试目录", permission = "", sort = 88, enabled = true, moduleId = 1, visible = true });

        var after = (await (await c.GetAsync("/api/v1/personal/menu?moduleId=1")).ReadEnvelope()).GetProperty("data").GetArrayLength();
        Assert.Equal(before + 1, after);   // 即时多一个根(非陈旧)
    }
}
