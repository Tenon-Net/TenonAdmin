using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 验证用户/机构管理的数据范围，以及按 Id 操作与列表的可见性一致。
/// </summary>
public class UserDataScopeTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>
    /// Helper: create a role with specific menu permissions and DataScopeType.Org,
    /// then create a user assigned to that role and a specific org.
    /// Returns the user's account name.
    /// </summary>
    private static async Task<string> CreateScopedUser(
        IServiceProvider sp, long orgId, string suffix, IReadOnlyList<long> menuIds,
        DataScopeType scopeType = DataScopeType.Org)
    {
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var rbac = sp.GetRequiredService<IRbacService>();
        var role = new SysRole
        {
            Name = "scope-role-" + suffix,
            Code = "scope-" + Guid.CreateVersion7().ToString("N")[..8],
            Enabled = true,
        };
        await roles.InsertAsync(role);
        await rbac.SetRoleMenusAsync(role.Id, menuIds);
        await rbac.SetRoleDataScopeAsync(role.Id, scopeType);

        var account = "scope-" + Guid.CreateVersion7().ToString("N")[..8];
        await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = account,
            Password = "Scope@123456",
            Name = "范围用户" + suffix,
            Enabled = true,
            OrgId = orgId,
            RoleIds = [role.Id],
        });
        return account;
    }

    [Fact]
    public async Task Non_superadmin_sees_only_users_in_scope_orgs()
    {
        const long orgTech = 3;   // 技术部(种子机构)
        const long orgHr = 7;     // 人事部(种子机构)
        using var f = new AdminAppFactory();

        string scopedAccount;
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            // 11 = 用户-分页 permission seed menu
            scopedAccount = await CreateScopedUser(sp, orgTech, "list", [11]);

            // Create a user in HR (out of scope for the tech user)
            await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = "hr-user-" + Guid.CreateVersion7().ToString("N")[..8],
                Password = "Test@123456",
                Name = "人事部用户",
                Enabled = true,
                OrgId = orgHr,
            });
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(scopedAccount, "Scope@123456"));

        var page = await (await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=200")).ReadEnvelope();
        Assert.Equal(0, page.GetProperty("code").GetInt32());

        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        // Scoped user should not see users from HR
        Assert.DoesNotContain(items, u => u.GetProperty("name").GetString() == "人事部用户");
        // Scoped user should see themselves (tech org)
        Assert.Contains(items, u => u.GetProperty("account").GetString() == scopedAccount);
    }

    [Fact]
    public async Task Superadmin_sees_all_users()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));

        using (var scope = f.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = "all-vis-" + Guid.CreateVersion7().ToString("N")[..8],
                Password = "Test@123456",
                Name = "全可见用户",
                Enabled = true,
                OrgId = 7,   // HR
            });
        }

        var page = await (await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=200")).ReadEnvelope();
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, u => u.GetProperty("name").GetString() == "全可见用户");
    }

    [Theory]
    [InlineData(7L)]
    [InlineData(null)]
    public async Task Non_superadmin_cannot_add_user_to_out_of_scope_org(long? targetOrgId)
    {
        const long orgTech = 3;
        using var f = new AdminAppFactory();

        string scopedAccount;
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            // 12 = 用户-新增 permission
            scopedAccount = await CreateScopedUser(sp, orgTech, "add", [12]);
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(scopedAccount, "Scope@123456"));

        // 范围外机构和未分配机构都不可创建，失败不得留下用户记录。
        var account = "oos-" + Guid.CreateVersion7().ToString("N")[..8];
        var result = await (await c.PostJson("/api/v1/sys/user", new
        {
            account,
            password = "Test@123456",
            name = "越权新增",
            enabled = true,
            orgId = targetOrgId,
            roleIds = Array.Empty<long>(),
        })).ReadEnvelope();

        Assert.Equal((int)ErrorCode.OrgOutOfScope, result.GetProperty("code").GetInt32());
        using var check = f.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<IRepository<SysUser>>()
            .AnyAsync(u => u.Account == account));
    }

    [Fact]
    public async Task Non_superadmin_org_list_returns_only_scoped_plus_ancestors()
    {
        const long orgTech = 3;
        using var f = new AdminAppFactory();

        string scopedAccount;
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            // 13 = 机构-列表 permission
            scopedAccount = await CreateScopedUser(sp, orgTech, "orglist", [13]);
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(scopedAccount, "Scope@123456"));

        var list = (await (await c.GetAsync("/api/v1/sys/org/list")).ReadEnvelope())
            .GetProperty("data").EnumerateArray().Select(o => o.GetProperty("id").GetInt64()).ToList();

        // Tech user (orgId=3, scope=Org → OrgIds=[3]) should see:
        //   org 3 (技术部, in scope) and org 1 (榫卯科技, ancestor of 3)
        Assert.Contains(3L, list);
        Assert.Contains(1L, list);
        // Should NOT see HR (7) which is a sibling, not ancestor
        Assert.DoesNotContain(7L, list);
    }

    [Theory]
    [InlineData("get", 7L)]
    [InlineData("update", 7L)]
    [InlineData("delete", 7L)]
    [InlineData("batch-delete", 7L)]
    [InlineData("password", 7L)]
    [InlineData("enabled", 7L)]
    [InlineData("get", null)]
    [InlineData("update", null)]
    [InlineData("delete", null)]
    [InlineData("batch-delete", null)]
    [InlineData("password", null)]
    [InlineData("enabled", null)]
    public async Task Id_operations_reject_invisible_users_without_side_effects_and_allow_scoped_users(
        string operation, long? targetOrgId)
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var actor = await CreateScopedUser(sp, 3, "id-guard", [50, 51, 52, 53, 54, 83]);
        var target = await CreateTarget(sp, "outside", targetOrgId);
        var inside = await CreateTarget(sp, "inside", 3);
        using var client = f.CreateClient();
        WithToken(client, await client.LoginToken(actor, "Scope@123456"));
        using var targetClient = f.CreateClient();
        WithToken(targetClient, await targetClient.LoginToken(target.Account, "Test@123456"));

        // 更新故意把范围外用户移入自己的机构；批删先放范围内用户，确保整批先校验再写入。
        var denied = await (await Operate(client, operation, target.Id, inside.Id)).ReadEnvelope();
        Assert.Equal((int)ErrorCode.OrgOutOfScope, denied.GetProperty("code").GetInt32());
        await AssertUnchanged(sp, target);
        await AssertUnchanged(sp, inside);
        Assert.Equal(System.Net.HttpStatusCode.OK,
            (await targetClient.GetAsync("/api/v1/personal/profile")).StatusCode);

        var allowed = await (await Operate(client, operation, inside.Id, inside.Id)).ReadEnvelope();
        Assert.Equal(0, allowed.GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData(false, 7L)]
    [InlineData(false, null)]
    [InlineData(true, 7L)]
    [InlineData(true, null)]
    public async Task Unrestricted_and_superadmin_can_manage_users_across_orgs(bool superAdmin, long? targetOrgId)
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var actor = superAdmin ? "superAdmin"
            : await CreateScopedUser(sp, 3, "all", [50, 51, 52, 53, 54, 83], DataScopeType.All);
        using var client = f.CreateClient();
        WithToken(client, await client.LoginToken(actor, superAdmin ? "Test@123456" : "Scope@123456"));

        foreach (var operation in new[] { "get", "update", "delete", "batch-delete", "password", "enabled" })
        {
            var target = await CreateTarget(sp, operation, targetOrgId);
            var result = await (await Operate(client, operation, target.Id, target.Id)).ReadEnvelope();
            Assert.Equal(0, result.GetProperty("code").GetInt32());
        }
    }

    [Theory]
    [InlineData(DataScopeType.Self, true)]
    [InlineData(DataScopeType.Custom, false)]
    public async Task Empty_org_scope_only_allows_self_when_explicitly_included(DataScopeType scopeType, bool includesSelf)
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var actor = await CreateScopedUser(sp, 3, "empty", [50], scopeType);
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var self = (await users.GetFirstAsync(u => u.Account == actor))!;
        var peer = await CreateTarget(sp, "peer", 3);
        using var client = f.CreateClient();
        WithToken(client, await client.LoginToken(actor, "Scope@123456"));

        var selfResult = await (await client.GetAsync($"/api/v1/sys/user/{self.Id}")).ReadEnvelope();
        Assert.Equal(includesSelf ? 0 : (int)ErrorCode.OrgOutOfScope, selfResult.GetProperty("code").GetInt32());
        var peerResult = await (await client.GetAsync($"/api/v1/sys/user/{peer.Id}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.OrgOutOfScope, peerResult.GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData(7L)]
    [InlineData(null)]
    public async Task Update_rejects_moving_a_visible_user_to_an_out_of_scope_org(long? targetOrgId)
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var actor = await CreateScopedUser(sp, 3, "move", [51]);
        var target = await CreateTarget(sp, "move-target", 3);
        using var client = f.CreateClient();
        WithToken(client, await client.LoginToken(actor, "Scope@123456"));

        var result = await (await client.PutJson($"/api/v1/sys/user/{target.Id}",
            new { name = "改名", enabled = true, orgId = targetOrgId, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.OrgOutOfScope, result.GetProperty("code").GetInt32());
        await AssertUnchanged(sp, target);
    }

    private static async Task<SysUser> CreateTarget(IServiceProvider sp, string suffix, long? orgId)
    {
        var created = await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = "target-" + suffix, Password = "Test@123456", Name = "目标用户",
            Enabled = true, OrgId = orgId,
        });
        return (await sp.GetRequiredService<IRepository<SysUser>>().GetByIdAsync(created.Id))!;
    }

    private static Task<HttpResponseMessage> Operate(HttpClient client, string operation, long id, long insideId) =>
        operation switch
        {
            "get" => client.GetAsync($"/api/v1/sys/user/{id}"),
            "update" => client.PutJson($"/api/v1/sys/user/{id}",
                new { name = "已更新", enabled = true, orgId = 3, roleIds = Array.Empty<long>() }),
            "delete" => client.DeleteAsync($"/api/v1/sys/user/{id}"),
            "batch-delete" => client.PostJson("/api/v1/sys/user/batch-delete", new { ids = new[] { insideId, id } }),
            "password" => client.PutJson($"/api/v1/sys/user/{id}/password", new { newPassword = "Changed@123456" }),
            "enabled" => client.PutJson($"/api/v1/sys/user/{id}/enabled", new { enabled = false }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static async Task AssertUnchanged(IServiceProvider sp, SysUser before)
    {
        var after = await sp.GetRequiredService<IRepository<SysUser>>().GetByIdAsync(before.Id);
        Assert.NotNull(after);
        Assert.Equal((before.Name, before.OrgId, before.Password, before.Enabled, before.IsDelete),
            (after.Name, after.OrgId, after.Password, after.Enabled, after.IsDelete));
    }
}
