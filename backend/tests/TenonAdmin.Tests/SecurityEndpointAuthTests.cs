using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 安全诊断两条路由的未登录、无权限与关闭模块。成功路径见 <see cref="SecurityBaselinePrecheckTests"/>。
/// </summary>
public class SecurityEndpointAuthTests
{
    private static readonly string[] Paths =
    [
        "/api/v1/sys/security/baseline",
        "/api/v1/sys/level3/precheck",
    ];

    [Fact]
    public async Task Anonymous_baseline_is_401_and_40006()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();
        foreach (var path in Paths)
        {
            var resp = await anon.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.Equal(40006, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
        }
    }

    [Fact]
    public async Task User_without_baseline_permission_is_403()
    {
        using var f = new AdminAppFactory();
        var c = await PingOnly(f);
        foreach (var path in Paths)
        {
            var resp = await c.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Equal(41001, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
        }
    }

    [Fact]
    public async Task Disabled_module_hides_both_paths()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Security"] };
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        foreach (var path in Paths)
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(path)).StatusCode);
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
