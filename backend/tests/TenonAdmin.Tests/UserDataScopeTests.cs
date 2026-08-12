using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// QA08: non-superadmin user/org management scoped by data scope.
/// Verifies BuildListQuery, AddAsync, UpdateAsync org scope guards, and OrgService scoping.
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
        IServiceProvider sp, long orgId, string suffix, IReadOnlyList<long> menuIds)
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
        await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.Org);

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

    [Fact]
    public async Task Non_superadmin_cannot_add_user_to_out_of_scope_org()
    {
        const long orgTech = 3;
        const long orgHr = 7;
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

        // Try to add user to HR (out of scope) → should fail with OrgOutOfScope
        var result = await (await c.PostJson("/api/v1/sys/user", new
        {
            account = "oos-" + Guid.CreateVersion7().ToString("N")[..8],
            password = "Test@123456",
            name = "越权新增",
            enabled = true,
            orgId = orgHr,
            roleIds = Array.Empty<long>(),
        })).ReadEnvelope();

        Assert.Equal((int)ErrorCode.OrgOutOfScope, result.GetProperty("code").GetInt32());
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
}
