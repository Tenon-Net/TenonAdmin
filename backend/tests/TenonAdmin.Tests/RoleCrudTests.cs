using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 角色 CRUD + 删除级联的 HTTP 级回归。业务失败走统一信封 HTTP 200 + 业务码(见 AdminExceptionFilter),
/// 故断言落在信封 code 上。级联是本模块唯一比职位复杂处,单独立一条用例锁死。
/// </summary>
public class RoleCrudTests
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

    [Fact]
    public async Task SuperAdmin_can_crud_role()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 新增
        var add = await c.PostJson("/api/v1/sys/role/add", new { name = "运维", code = "ops", sort = 5, enabled = true, remark = "运维角色" });
        var addEnv = await add.ReadEnvelope();
        Assert.Equal(0, addEnv.GetProperty("code").GetInt32());
        var newId = addEnv.GetProperty("data").GetInt64();

        // 分页含新角色
        var page = (await (await c.GetAsync("/api/v1/sys/role/page?Current=1&Size=50")).ReadEnvelope()).GetProperty("data");
        var ids = page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(newId, ids);

        // 取单个
        var get = await (await c.GetAsync($"/api/v1/sys/role/{newId}")).ReadEnvelope();
        Assert.Equal("ops", get.GetProperty("data").GetProperty("code").GetString());

        // 更新
        var upd = await c.PutJson($"/api/v1/sys/role/{newId}", new { name = "运维V2", code = "ops", sort = 6, enabled = true, remark = "" });
        Assert.Equal(0, (await upd.ReadEnvelope()).GetProperty("code").GetInt32());
        var reGet = await (await c.GetAsync($"/api/v1/sys/role/{newId}")).ReadEnvelope();
        Assert.Equal("运维V2", reGet.GetProperty("data").GetProperty("name").GetString());

        // 删除
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/role/{newId}")).ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Duplicate_code_is_rejected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        Assert.Equal(0, (await (await c.PostJson("/api/v1/sys/role/add", new { name = "A", code = "dup", sort = 1, enabled = true })).ReadEnvelope()).GetProperty("code").GetInt32());
        var second = await c.PostJson("/api/v1/sys/role/add", new { name = "B", code = "dup", sort = 2, enabled = true });
        Assert.Equal(42018, (await second.ReadEnvelope()).GetProperty("code").GetInt32());  // RoleCodeExists
    }

    [Fact]
    public async Task Soft_delete_preserves_associations_purge_cleans_them()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 建角色 + 授菜单 + 配数据范围 + 挂到一个用户
        var roleId = (await (await c.PostJson("/api/v1/sys/role/add", new { name = "级联", code = "cascade", sort = 1, enabled = true })).ReadEnvelope()).GetProperty("data").GetInt64();
        await c.PutJson("/api/v1/sys/role/menu", new { roleId, menuIds = new[] { 10L } });
        await c.PutJson("/api/v1/sys/role/datascope", new { roleId, scopeType = 3 });  // OrgAndChildren
        var userId = (await (await c.PostJson("/api/v1/sys/user", new { account = "role_cascade_u", password = "Test@123456", name = "级联用户", enabled = true, roleIds = new[] { roleId } })).ReadEnvelope()).GetProperty("data").GetProperty("id").GetInt64();

        // 前置:三组关联都在
        Assert.Contains(10L, (await (await c.GetAsync($"/api/v1/sys/role/{roleId}/menus")).ReadEnvelope()).GetProperty("data").EnumerateArray().Select(x => x.GetInt64()));
        Assert.Equal(JsonValueKind.Object, (await (await c.GetAsync($"/api/v1/sys/role/{roleId}/datascope")).ReadEnvelope()).GetProperty("data").ValueKind);
        Assert.Contains(roleId, (await (await c.GetAsync($"/api/v1/sys/user/{userId}")).ReadEnvelope()).GetProperty("data").GetProperty("roleIds").EnumerateArray().Select(x => x.GetInt64()));

        // 软删角色(QA23):关联保留,恢复即可用
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/role/{roleId}")).ReadEnvelope()).GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            Assert.NotEmpty(await db.Queryable<SysRoleMenu>().Where(x => x.RoleId == roleId).ToListAsync());
            Assert.NotEmpty(await db.Queryable<SysRoleDataScope>().Where(x => x.RoleId == roleId).ToListAsync());
            Assert.NotEmpty(await db.Queryable<SysUserRole>().Where(x => x.RoleId == roleId).ToListAsync());
        }

        // 回收站硬删(QA23):真正清关联
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/recycle/role/{roleId}")).ReadEnvelope()).GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            Assert.Empty(await db.Queryable<SysRoleMenu>().Where(x => x.RoleId == roleId).ToListAsync());
            Assert.Empty(await db.Queryable<SysRoleDataScope>().Where(x => x.RoleId == roleId).ToListAsync());
            Assert.Empty(await db.Queryable<SysUserRole>().Where(x => x.RoleId == roleId).ToListAsync());
        }

        Assert.DoesNotContain(roleId, (await (await c.GetAsync($"/api/v1/sys/user/{userId}")).ReadEnvelope()).GetProperty("data").GetProperty("roleIds").EnumerateArray().Select(x => x.GetInt64()));
    }
}
