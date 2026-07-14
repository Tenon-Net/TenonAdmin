using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 回收站(<c>/sys/recycle/{type}</c>)——此前零测试,而 Purge 是<b>不可逆硬删</b>。
/// 锁死:非法 type 拒绝、软删→列表→恢复回环、恢复撞唯一键报专用码、硬删真的删掉、硬删留操作日志。
/// </summary>
public class RecycleBinTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<long> AddRole(HttpClient c, string name, string code) =>
        (await (await c.PostJson("/api/v1/sys/role/add",
            new { name, code, sort = 0, enabled = true, remark = "" })).ReadEnvelope())
            .GetProperty("data").GetInt64();

    private static async Task<List<JsonElement>> RecyclePage(HttpClient c, string type) =>
        (await (await c.GetAsync($"/api/v1/sys/recycle/{type}/page?Current=1&Size=100")).ReadEnvelope())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Invalid_type_is_rejected()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var env = await (await admin.GetAsync("/api/v1/sys/recycle/nonsense/page?Current=1&Size=10")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.RecycleInvalidType, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Soft_deleted_row_appears_then_restore_brings_it_back()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var id = await AddRole(admin, "temp", "temp-code");
        await admin.DeleteAsync($"/api/v1/sys/role/{id}");   // 软删

        // 正常分页看不到,回收站看得到
        var recycled = await RecyclePage(admin, "role");
        Assert.Contains(recycled, e => e.GetProperty("id").GetInt64() == id);

        // 恢复 → 回到正常查询
        var restore = await (await admin.PostJson($"/api/v1/sys/recycle/role/{id}/restore", new { })).ReadEnvelope();
        Assert.Equal(0, restore.GetProperty("code").GetInt32());

        var detail = await (await admin.GetAsync($"/api/v1/sys/role/{id}")).ReadEnvelope();
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        Assert.Equal("temp", detail.GetProperty("data").GetProperty("name").GetString());
        Assert.DoesNotContain(await RecyclePage(admin, "role"), e => e.GetProperty("id").GetInt64() == id);
    }

    [Fact]
    public async Task Restore_conflicting_unique_code_is_rejected()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var first = await AddRole(admin, "first", "dup");
        await admin.DeleteAsync($"/api/v1/sys/role/{first}");   // 软删 → 释放 Code 唯一占位
        await AddRole(admin, "second", "dup");                  // 新的活行占用 "dup"

        // 恢复被删的那个 → 想把 Code 改回 "dup" 却撞上活行 → 专用码,不是原生 500
        var env = await (await admin.PostJson($"/api/v1/sys/recycle/role/{first}/restore", new { })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.RecycleUniqueConflict, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Purge_hard_deletes_and_is_audit_logged()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var id = await AddRole(admin, "doomed", "doomed-code");
        await admin.DeleteAsync($"/api/v1/sys/role/{id}");   // 软删

        var purge = await (await admin.DeleteAsync($"/api/v1/sys/recycle/role/{id}")).ReadEnvelope();
        Assert.Equal(0, purge.GetProperty("code").GetInt32());

        // 硬删后连回收站也看不到了
        Assert.DoesNotContain(await RecyclePage(admin, "role"), e => e.GetProperty("id").GetInt64() == id);

        // 不可逆硬删必须留操作日志
        var logs = (await (await admin.GetAsync("/api/v1/sys/log/op/page?Current=1&Size=100")).ReadEnvelope())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(logs, l => l.GetProperty("httpMethod").GetString() == "DELETE"
            && l.GetProperty("path").GetString() == $"/api/v1/sys/recycle/role/{id}");
    }
}
