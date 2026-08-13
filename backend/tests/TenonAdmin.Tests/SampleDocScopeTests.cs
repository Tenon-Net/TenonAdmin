using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.TestHost;

namespace TenonAdmin.Tests;

/// <summary>
/// 机构隔离业务表(<see cref="SampleDoc"/>)的 HTTP 级回归:验证消费方 DataEntity 模块端到端可用,
/// 且数据范围写守卫经真实登录用户的范围解析后仍生效(越权改删他机构行被拒)。
/// </summary>
public class SampleDocScopeTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task SampleDoc_superadmin_crud_smoke()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));

        var id = (await (await c.PostJson("/api/v1/sample/doc", new { title = "hello" })).ReadEnvelope())
            .GetProperty("data").GetInt64();

        var list = (await (await c.GetAsync("/api/v1/sample/doc")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(1, list.GetArrayLength());

        Assert.True((await (await c.PutJson($"/api/v1/sample/doc/{id}", new { title = "world" })).ReadEnvelope())
            .GetProperty("data").GetBoolean());   // 超管改自己建的行 → 成功

        Assert.True((await (await c.DeleteAsync($"/api/v1/sample/doc/{id}")).ReadEnvelope())
            .GetProperty("data").GetBoolean());    // 超管删 → 成功

        Assert.Equal(0, (await (await c.GetAsync("/api/v1/sample/doc")).ReadEnvelope())
            .GetProperty("data").GetArrayLength()); // 已删,列表空
    }

    [Fact]
    public async Task Cross_org_write_denied_through_real_auth_pipeline()
    {
        const long orgA = 910001, orgB = 910002;
        using var f = new AdminAppFactory();

        // 后台造:一个授了"删 SampleDoc"权限 + 本机构数据范围的机构 A 用户,并插机构 A/B 各一行 doc
        long docAId, docBId;
        string account, password = "Scope@123456";
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;

            // 授权菜单:权限码 = DELETE 路由(权限码即规范化路由,须字节匹配)
            var menus = sp.GetRequiredService<IRepository<SysMenu>>();
            var delMenu = new SysMenu
            {
                ParentId = 1, Type = MenuType.Button, Title = "删除示例文档",
                Permission = "DELETE:/api/v1/sample/doc/{id}", Enabled = true, Visible = true,
            };
            await menus.InsertAsync(delMenu);

            // 角色:授该菜单 + 数据范围=本机构
            var roles = sp.GetRequiredService<IRepository<SysRole>>();
            var rbac = sp.GetRequiredService<IRbacService>();
            var role = new SysRole { Name = "范围测试角色", Code = "scope-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
            await roles.InsertAsync(role);
            await rbac.SetRoleMenusAsync(role.Id, [delMenu.Id]);
            await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.Org);   // 本机构:范围解析为用户 OrgId

            // 机构 A 用户
            account = "scope-" + Guid.CreateVersion7().ToString("N")[..8];
            await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = account, Password = password, Name = "机构A用户", Enabled = true, OrgId = orgA, RoleIds = [role.Id],
            });

            // 直插两机构各一行(此上下文不受限,显式设 CreateOrgId)
            var docs = sp.GetRequiredService<IRepository<SampleDoc>>();
            var a = new SampleDoc { Title = "A 的文档", CreateOrgId = orgA };
            var b = new SampleDoc { Title = "B 的文档", CreateOrgId = orgB };
            await docs.InsertAsync(a);
            await docs.InsertAsync(b);
            docAId = a.Id;
            docBId = b.Id;
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        // 删他机构(B)的行 → 守卫拒写,data=false(表现为"改不动/查不到",非 403)
        Assert.False((await (await c.DeleteAsync($"/api/v1/sample/doc/{docBId}")).ReadEnvelope())
            .GetProperty("data").GetBoolean());

        // 删本机构(A)的行 → 成功
        Assert.True((await (await c.DeleteAsync($"/api/v1/sample/doc/{docAId}")).ReadEnvelope())
            .GetProperty("data").GetBoolean());

        // 复核:B 的行未被越权删(超管视角仍在)
        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));
        var titles = (await (await c.GetAsync("/api/v1/sample/doc")).ReadEnvelope()).GetProperty("data")
            .EnumerateArray().Select(d => d.GetProperty("title").GetString()).ToList();
        Assert.Contains("B 的文档", titles);      // B 完好
        Assert.DoesNotContain("A 的文档", titles); // A 已被本机构用户删
    }

    [Fact]
    public async Task ActiveSession_endpoint_still_filters_by_data_scope()
    {
        const long orgA = 910011, orgB = 910012;
        using var f = new AdminAppFactory();

        string account, password = "Scope@123456";
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var roles = sp.GetRequiredService<IRepository<SysRole>>();
            var rbac = sp.GetRequiredService<IRbacService>();
            var role = new SysRole { Name = "仅登录范围", Code = "as-scope-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
            await roles.InsertAsync(role);
            await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.Org);

            account = "as-scope-" + Guid.CreateVersion7().ToString("N")[..8];
            await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = account, Password = password, Name = "机构A仅登录", Enabled = true, OrgId = orgA, RoleIds = [role.Id],
            });

            var docs = sp.GetRequiredService<IRepository<SampleDoc>>();
            await docs.InsertRangeAsync(
            [
                new SampleDoc { Title = "A-mine", CreateOrgId = orgA },
                new SampleDoc { Title = "B-mine", CreateOrgId = orgB },
            ]);
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        // 无 GET:/api/v1/sample/doc 权限,但 mine 是 [ActiveSession];范围必须仍是本机构,不能因漏绑而看到 B
        var titles = (await (await c.GetAsync("/api/v1/sample/doc/mine")).ReadEnvelope()).GetProperty("data")
            .EnumerateArray().Select(d => d.GetProperty("title").GetString()).ToList();
        Assert.Contains("A-mine", titles);
        Assert.DoesNotContain("B-mine", titles);

        WithToken(c, await c.LoginToken("superAdmin", "Test@123456"));
        var all = (await (await c.GetAsync("/api/v1/sample/doc/mine")).ReadEnvelope()).GetProperty("data")
            .EnumerateArray().Select(d => d.GetProperty("title").GetString()).ToList();
        Assert.Contains("A-mine", all);
        Assert.Contains("B-mine", all);
    }
}
