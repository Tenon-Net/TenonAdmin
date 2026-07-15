using System.Net.Http.Headers;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 职位标准 CRUD 的 HTTP 级回归(增/查/分页/改/删 + 编码查重 + 查不存在)。
/// 行拖拽重排与安全排序已由 <see cref="PositionSortReorderTests"/> 单独覆盖,此处不重复。
/// </summary>
public class PositionCrudTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Add_position_then_get_by_id_returns_it()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var add = await c.PostJson("/api/v1/sys/position/add", new { name = "行政专员", code = "CRUD_ADD", sort = 1, enabled = true });
        var addEnv = await add.ReadEnvelope();
        Assert.Equal(0, addEnv.GetProperty("code").GetInt32());
        var newId = addEnv.GetProperty("data").GetInt64();

        var get = await (await c.GetAsync($"/api/v1/sys/position/{newId}")).ReadEnvelope();
        Assert.Equal(0, get.GetProperty("code").GetInt32());
        Assert.Equal("行政专员", get.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("CRUD_ADD", get.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Page_lists_the_created_position()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var addEnv = await (await c.PostJson("/api/v1/sys/position/add", new { name = "CRUD_PAGE", code = "CRUD_PAGE", sort = 1, enabled = true })).ReadEnvelope();
        var newId = addEnv.GetProperty("data").GetInt64();

        var page = (await (await c.GetAsync("/api/v1/sys/position/page?Current=1&Size=10")).ReadEnvelope()).GetProperty("data");
        Assert.True(page.GetProperty("total").GetInt64() >= 1);
        var ids = page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(newId, ids);
    }

    [Fact]
    public async Task Update_position_changes_are_reflected_on_get()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var addEnv = await (await c.PostJson("/api/v1/sys/position/add", new { name = "CRUD_UPD", code = "CRUD_UPD", sort = 1, enabled = true })).ReadEnvelope();
        var newId = addEnv.GetProperty("data").GetInt64();

        var upd = await c.PutJson($"/api/v1/sys/position/{newId}", new { name = "CRUD_UPD_V2", code = "CRUD_UPD", sort = 2, enabled = false });
        Assert.Equal(0, (await upd.ReadEnvelope()).GetProperty("code").GetInt32());

        var reGet = (await (await c.GetAsync($"/api/v1/sys/position/{newId}")).ReadEnvelope()).GetProperty("data");
        Assert.Equal("CRUD_UPD_V2", reGet.GetProperty("name").GetString());
        Assert.False(reGet.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Delete_position_removes_it_from_page()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var addEnv = await (await c.PostJson("/api/v1/sys/position/add", new { name = "CRUD_DEL", code = "CRUD_DEL", sort = 1, enabled = true })).ReadEnvelope();
        var newId = addEnv.GetProperty("data").GetInt64();

        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/position/{newId}")).ReadEnvelope()).GetProperty("code").GetInt32());

        var page = (await (await c.GetAsync("/api/v1/sys/position/page?Current=1&Size=50")).ReadEnvelope()).GetProperty("data");
        var ids = page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("id").GetInt64()).ToList();
        Assert.DoesNotContain(newId, ids);
    }

    [Fact]
    public async Task Add_with_duplicate_code_returns_PositionCodeExists()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var first = await (await c.PostJson("/api/v1/sys/position/add", new { name = "CRUD_DUP_1", code = "CRUD_DUP", sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal(0, first.GetProperty("code").GetInt32());

        var dup = await (await c.PostJson("/api/v1/sys/position/add", new { name = "CRUD_DUP_2", code = "CRUD_DUP", sort = 2, enabled = true })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PositionCodeExists, dup.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Get_nonexistent_position_returns_PositionNotFound()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var got = await (await c.GetAsync("/api/v1/sys/position/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.PositionNotFound, got.GetProperty("code").GetInt32());
    }

}
