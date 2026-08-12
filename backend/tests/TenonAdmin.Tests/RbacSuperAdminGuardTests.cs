using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// QA09:数据范围配置仅超管可执行——即使普通用户拥有路由权限码也被拒绝。
/// </summary>
public class RbacSuperAdminGuardTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task NonSuperAdmin_with_route_permission_rejected_on_SetDataScope()
    {
        using var f = new AdminAppFactory();

        // Seed: 角色授予 menuId=4 ("PUT:/api/v1/sys/role/datascope") + 普通用户
        long targetRoleId;
        string account, password;
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var roles = sp.GetRequiredService<IRepository<SysRole>>();
            var rbac = sp.GetRequiredService<IRbacService>();
            var users = sp.GetRequiredService<IUserService>();

            // 被操作的目标角色
            var targetRole = new SysRole { Name = "目标角色", Code = "target-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
            await roles.InsertAsync(targetRole);
            targetRoleId = targetRole.Id;

            // 操作者的角色:拥有数据范围路由权限
            var opRole = new SysRole { Name = "操作者角色", Code = "op-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
            await roles.InsertAsync(opRole);
            await rbac.SetRoleMenusAsync(opRole.Id, [4]);   // menuId=4 = PUT:/api/v1/sys/role/datascope

            account = "qa09-" + Guid.CreateVersion7().ToString("N")[..8];
            password = "Qa09@123456";
            await users.AddAsync(new AddUserInput { Account = account, Password = password, Name = "非超管操作者", Enabled = true, RoleIds = [opRole.Id] });
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        // 调用 SetDataScope 接口 → 应被拒绝(41003 SuperAdminRequired,业务码在信封中返回)
        var resp = await c.PutJson("/api/v1/sys/role/datascope", new { roleId = targetRoleId, scopeType = 1 });
        var env = await resp.ReadEnvelope();
        Assert.Equal(41003, env.GetProperty("code").GetInt32());
    }
}
