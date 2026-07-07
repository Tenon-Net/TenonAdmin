using System.Net;
using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 模块/应用 CRUD 的 HTTP 级回归(多应用门户)。业务失败走统一信封 HTTP 200 + 业务码
/// (见 AdminExceptionFilter),故断言落在信封 code 上。
/// </summary>
public class ModuleCrudTests
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
    public async Task SuperAdmin_can_crud_module()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 新增
        var add = await c.PostJson("/api/v1/sys/module/add", new { code = "crm", title = "客户管理", sort = 2, enabled = true });
        var addEnv = await add.ReadEnvelope();
        Assert.Equal(0, addEnv.GetProperty("code").GetInt32());
        var newId = addEnv.GetProperty("data").GetInt64();

        // 列表含新模块 + 内置 system
        var list = (await (await c.GetAsync("/api/v1/sys/module/list")).ReadEnvelope()).GetProperty("data");
        var ids = list.EnumerateArray().Select(m => m.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(newId, ids);
        Assert.Contains(1L, ids);   // 内置 system 模块

        // 取单个
        var get = await (await c.GetAsync($"/api/v1/sys/module/{newId}")).ReadEnvelope();
        Assert.Equal("crm", get.GetProperty("data").GetProperty("code").GetString());

        // 更新
        var upd = await c.PutJson($"/api/v1/sys/module/{newId}", new { code = "crm", title = "客户管理V2", sort = 3, enabled = true });
        Assert.Equal(0, (await upd.ReadEnvelope()).GetProperty("code").GetInt32());
        var reGet = await (await c.GetAsync($"/api/v1/sys/module/{newId}")).ReadEnvelope();
        Assert.Equal("客户管理V2", reGet.GetProperty("data").GetProperty("title").GetString());

        // 删除
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/module/{newId}")).ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Delete_builtin_system_module_is_protected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var del = await c.DeleteAsync("/api/v1/sys/module/1");   // 内置 system 模块
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);        // 业务失败仍是 200 信封
        Assert.Equal(42013, (await del.ReadEnvelope()).GetProperty("code").GetInt32());  // ModuleProtected
    }

    [Fact]
    public async Task Duplicate_code_is_rejected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        Assert.Equal(0, (await (await c.PostJson("/api/v1/sys/module/add", new { code = "dup", title = "A", sort = 1, enabled = true })).ReadEnvelope()).GetProperty("code").GetInt32());
        var second = await c.PostJson("/api/v1/sys/module/add", new { code = "dup", title = "B", sort = 2, enabled = true });
        Assert.Equal(42012, (await second.ReadEnvelope()).GetProperty("code").GetInt32());  // ModuleCodeExists
    }
}
