using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// QA36:角色委派集中管控——
/// (1) 角色定义(CRUD)仅超管;(2) 角色授权面(菜单/数据范围)仅超管;
/// (3) 非超管可给范围内用户授予可转授角色;(4) 非超管不可授予不可转授角色;
/// (5) 超管无上述限制。
/// </summary>
public class RoleDelegationTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        return WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));
    }

    /// <summary>搭建:一个非超管操作者(有角色/用户路由权限) + 一个可转授角色 + 一个不可转授角色 + 一个目标用户。</summary>
    private static async Task<(string Account, string Password, long DelegatableRoleId, long NonDelegatableRoleId, long TargetUserId)>
        SeedDelegationScenario(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var rbac = sp.GetRequiredService<IRbacService>();
        var users = sp.GetRequiredService<IUserService>();

        // 可转授角色
        var delegatable = new SysRole { Name = "可转授", Code = "dlg-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true, IsDelegatable = true };
        await roles.InsertAsync(delegatable);

        // 不可转授角色
        var nonDelegatable = new SysRole { Name = "不可转授", Code = "ndl-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true, IsDelegatable = false };
        await roles.InsertAsync(nonDelegatable);

        // 操作者角色:赋予用户管理 + 角色管理路由权限(通过授菜单)
        var opRole = new SysRole { Name = "操作者角色", Code = "opr-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true, IsDelegatable = true };
        await roles.InsertAsync(opRole);
        // menuId 5 = POST:/api/v1/sys/user, 6 = PUT:/api/v1/sys/user/{id},
        // 3 = PUT:/api/v1/sys/role/{id}, 1 = POST:/api/v1/sys/role/add, 2 = DELETE:/api/v1/sys/role/{id}
        // 7 = PUT:/api/v1/sys/role/users
        // 通过获取全部菜单来确定需要哪些 menuId 不太可靠;直接授予全量使路由校验通过
        // (真实的路由权限已经过了,QA36 的看门人是 EnsureSuperAdmin 和 RoleGrantPolicy)
        var allMenuIds = await sp.GetRequiredService<IRepository<SysMenu>>().AsQueryable().Select(m => m.Id).ToListAsync();
        await rbac.SetRoleMenusAsync(opRole.Id, allMenuIds);

        // 操作者角色:数据范围=全部(使目标用户在范围内,以测试 RoleGrantPolicy 的角色判定而非范围判定)
        await rbac.SetRoleDataScopeAsync(opRole.Id, DataScopeType.All);

        var account = "qa36-" + Guid.CreateVersion7().ToString("N")[..8];
        var password = "Qa36@123456";
        await users.AddAsync(new AddUserInput { Account = account, Password = password, Name = "QA36操作者", Enabled = true, RoleIds = [opRole.Id] });

        // 目标用户(同机构——默认数据范围为本机构)
        var target = await users.AddAsync(new AddUserInput { Account = "qa36tgt-" + Guid.CreateVersion7().ToString("N")[..8], Password = "Tgt@123456", Name = "目标用户", Enabled = true, RoleIds = [] });

        return (account, password, delegatable.Id, nonDelegatable.Id, target.Id);
    }

    [Fact]
    public async Task NonSuperAdmin_cannot_create_role()
    {
        using var f = new AdminAppFactory();
        var (account, password, _, _, _) = await SeedDelegationScenario(f);

        var c = WithToken(f.CreateClient(), await f.CreateClient().LoginToken(account, password));
        var resp = await c.PostJson("/api/v1/sys/role/add", new { name = "偷建", code = "steal", sort = 1, enabled = true });
        var env = await resp.ReadEnvelope();
        Assert.Equal(41003, env.GetProperty("code").GetInt32());  // SuperAdminRequired
    }

    [Fact]
    public async Task NonSuperAdmin_cannot_update_role()
    {
        using var f = new AdminAppFactory();
        var (account, password, delegatableRoleId, _, _) = await SeedDelegationScenario(f);

        var c = WithToken(f.CreateClient(), await f.CreateClient().LoginToken(account, password));
        var resp = await c.PutJson($"/api/v1/sys/role/{delegatableRoleId}", new { name = "改名", code = "dlg", sort = 1, enabled = true });
        var env = await resp.ReadEnvelope();
        Assert.Equal(41003, env.GetProperty("code").GetInt32());  // SuperAdminRequired
    }

    [Fact]
    public async Task NonSuperAdmin_cannot_delete_role()
    {
        using var f = new AdminAppFactory();
        var (account, password, delegatableRoleId, _, _) = await SeedDelegationScenario(f);

        var c = WithToken(f.CreateClient(), await f.CreateClient().LoginToken(account, password));
        var resp = await c.DeleteAsync($"/api/v1/sys/role/{delegatableRoleId}");
        var env = await resp.ReadEnvelope();
        Assert.Equal(41003, env.GetProperty("code").GetInt32());  // SuperAdminRequired
    }

    [Fact]
    public async Task NonSuperAdmin_cannot_SetRoleMenus()
    {
        using var f = new AdminAppFactory();
        var (account, password, delegatableRoleId, _, _) = await SeedDelegationScenario(f);

        var c = WithToken(f.CreateClient(), await f.CreateClient().LoginToken(account, password));
        var resp = await c.PutJson("/api/v1/sys/role/menu", new { roleId = delegatableRoleId, menuIds = new[] { 1L } });
        var env = await resp.ReadEnvelope();
        Assert.Equal(41003, env.GetProperty("code").GetInt32());  // SuperAdminRequired
    }

    [Fact]
    public async Task NonSuperAdmin_can_assign_delegatable_role_to_in_scope_user()
    {
        using var f = new AdminAppFactory();
        var (account, password, delegatableRoleId, _, targetUserId) = await SeedDelegationScenario(f);

        var c = WithToken(f.CreateClient(), await f.CreateClient().LoginToken(account, password));
        var resp = await c.PutJson("/api/v1/sys/role/users", new { roleId = delegatableRoleId, userIds = new[] { targetUserId } });
        var env = await resp.ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task NonSuperAdmin_cannot_assign_non_delegatable_role()
    {
        using var f = new AdminAppFactory();
        var (account, password, _, nonDelegatableRoleId, targetUserId) = await SeedDelegationScenario(f);

        var c = WithToken(f.CreateClient(), await f.CreateClient().LoginToken(account, password));
        var resp = await c.PutJson("/api/v1/sys/role/users", new { roleId = nonDelegatableRoleId, userIds = new[] { targetUserId } });
        var env = await resp.ReadEnvelope();
        Assert.Equal(41004, env.GetProperty("code").GetInt32());  // RoleNotDelegatable
    }

    [Fact]
    public async Task SuperAdmin_can_do_all()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 建角色(不可转授)
        var addResp = await c.PostJson("/api/v1/sys/role/add", new { name = "超管建", code = "sa-" + Guid.CreateVersion7().ToString("N")[..8], sort = 1, enabled = true, isDelegatable = false });
        var env = await addResp.ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        var roleId = env.GetProperty("data").GetInt64();

        // 把不可转授角色授给用户(超管不受限)
        var targetResp = await c.PostJson("/api/v1/sys/user", new { account = "sa-tgt-" + Guid.CreateVersion7().ToString("N")[..8], password = "Test@123456", name = "目标", enabled = true, roleIds = new[] { roleId } });
        Assert.Equal(0, (await targetResp.ReadEnvelope()).GetProperty("code").GetInt32());

        // 改角色
        var updResp = await c.PutJson($"/api/v1/sys/role/{roleId}", new { name = "改了", code = "sa-" + Guid.CreateVersion7().ToString("N")[..8], sort = 2, enabled = true, isDelegatable = true });
        Assert.Equal(0, (await updResp.ReadEnvelope()).GetProperty("code").GetInt32());

        // 授菜单
        var menuResp = await c.PutJson("/api/v1/sys/role/menu", new { roleId, menuIds = new[] { 1L } });
        Assert.Equal(0, (await menuResp.ReadEnvelope()).GetProperty("code").GetInt32());

        // 删角色
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/role/{roleId}")).ReadEnvelope()).GetProperty("code").GetInt32());
    }
}
