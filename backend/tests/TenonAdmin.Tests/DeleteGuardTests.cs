using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// QA10: block delete of org/position with active users; block self-management operations.
/// </summary>
public class DeleteGuardTests
{
    // ── Org: cannot delete if users belong to it ─────────────────────

    [Fact]
    public async Task Delete_org_with_active_user_returns_OrgHasUsers()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        // Create a leaf org
        var orgEnv = await (await c.PostJson("/api/v1/sys/org/add",
            new { parentId = 0, name = "用户挂靠测试机构", code = "GUARD_ORG_1", category = "", sort = 0, enabled = true })).ReadEnvelope();
        var orgId = orgEnv.GetProperty("data").GetInt64();

        // Create a user assigned to that org
        await (await c.PostJson("/api/v1/sys/user",
            new { account = "guard-org-u", password = "Test@123456", name = "挂靠用户", enabled = true, orgId, roleIds = Array.Empty<long>() })).ReadEnvelope();

        // Attempt to delete the org → should fail
        var del = await (await c.DeleteAsync($"/api/v1/sys/org/{orgId}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.OrgHasUsers, del.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Delete_org_without_users_succeeds()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var orgEnv = await (await c.PostJson("/api/v1/sys/org/add",
            new { parentId = 0, name = "空机构", code = "GUARD_ORG_2", category = "", sort = 0, enabled = true })).ReadEnvelope();
        var orgId = orgEnv.GetProperty("data").GetInt64();

        var del = await (await c.DeleteAsync($"/api/v1/sys/org/{orgId}")).ReadEnvelope();
        Assert.Equal(0, del.GetProperty("code").GetInt32());
    }

    // ── Position: cannot delete if users hold it ─────────────────────

    [Fact]
    public async Task Delete_position_with_active_user_returns_PositionHasUsers()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        // Create a position
        var posEnv = await (await c.PostJson("/api/v1/sys/position/add",
            new { name = "用户挂靠测试岗位", code = "GUARD_POS_1", sort = 0, enabled = true })).ReadEnvelope();
        var posId = posEnv.GetProperty("data").GetInt64();

        // Create a user assigned to that position
        await (await c.PostJson("/api/v1/sys/user",
            new { account = "guard-pos-u", password = "Test@123456", name = "挂靠用户", enabled = true, positionId = posId, roleIds = Array.Empty<long>() })).ReadEnvelope();

        // Attempt to delete the position → should fail
        var del = await (await c.DeleteAsync($"/api/v1/sys/position/{posId}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PositionHasUsers, del.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Delete_position_without_users_succeeds()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var posEnv = await (await c.PostJson("/api/v1/sys/position/add",
            new { name = "空岗位", code = "GUARD_POS_2", sort = 0, enabled = true })).ReadEnvelope();
        var posId = posEnv.GetProperty("data").GetInt64();

        var del = await (await c.DeleteAsync($"/api/v1/sys/position/{posId}")).ReadEnvelope();
        Assert.Equal(0, del.GetProperty("code").GetInt32());
    }

    // ── Self-management: user cannot operate on self ─────────────────

    [Fact]
    public async Task User_cannot_delete_self()
    {
        using var f = new AdminAppFactory();

        long userId;
        string account = "self-del-" + Guid.CreateVersion7().ToString("N")[..8];
        string password = "Self@123456";
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var menus = sp.GetRequiredService<IRepository<SysMenu>>();
            var roles = sp.GetRequiredService<IRepository<SysRole>>();
            var rbac = sp.GetRequiredService<IRbacService>();
            var role = new SysRole { Name = "自删角色", Code = "self-del-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
            await roles.InsertAsync(role);
            // 52 = 用户-删除
            await rbac.SetRoleMenusAsync(role.Id, [52]);
            await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.All);

            var result = await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = account, Password = password, Name = "自删用户", Enabled = true, RoleIds = [role.Id],
            });
            userId = result.Id;
        }

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));

        var del = await (await c.DeleteAsync($"/api/v1/sys/user/{userId}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.CannotOperateSelf, del.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task User_cannot_disable_self()
    {
        using var f = new AdminAppFactory();

        long userId;
        string account = "self-dis-" + Guid.CreateVersion7().ToString("N")[..8];
        string password = "Self@123456";
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var roles = sp.GetRequiredService<IRepository<SysRole>>();
            var rbac = sp.GetRequiredService<IRbacService>();
            var role = new SysRole { Name = "自停角色", Code = "self-dis-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
            await roles.InsertAsync(role);
            // 54 = 用户-启停
            await rbac.SetRoleMenusAsync(role.Id, [54]);
            await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.All);

            var result = await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = account, Password = password, Name = "自停用户", Enabled = true, RoleIds = [role.Id],
            });
            userId = result.Id;
        }

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));

        var dis = await (await c.PutJson($"/api/v1/sys/user/{userId}/enabled", new { enabled = false })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.CannotOperateSelf, dis.GetProperty("code").GetInt32());
    }
}
