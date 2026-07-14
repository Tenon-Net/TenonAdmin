using System.Net.Http.Headers;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 机构改父的成环防护:只挡"父指向自己"不够——把某机构的父设成它自己的后代同样成环,
/// 整支子树会脱离根、从机构树 UI 消失且无法在 UI 修复。锁死:改父到后代 → OrgInvalidParent。
/// </summary>
public class OrgCycleTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<long> AddOrg(HttpClient c, string name, long parentId) =>
        (await (await c.PostJson("/api/v1/sys/org/add",
            new { parentId, name, code = "", category = "", sort = 0, enabled = true })).ReadEnvelope())
            .GetProperty("data").GetInt64();

    private static async Task<System.Text.Json.JsonElement> Update(HttpClient c, long id, long parentId, string name) =>
        await (await c.PutJson($"/api/v1/sys/org/{id}",
            new { parentId, name, code = "", category = "", sort = 0, enabled = true })).ReadEnvelope();

    [Fact]
    public async Task Cannot_reparent_org_under_its_own_descendant()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // A → B → C
        var a = await AddOrg(admin, "A", 0);
        var b = await AddOrg(admin, "B", a);
        var c = await AddOrg(admin, "C", b);

        // 把 A 的父设成它的孙 C → 成环,必须拒
        var underGrandchild = await Update(admin, a, c, "A");
        Assert.Equal((int)ErrorCode.OrgInvalidParent, underGrandchild.GetProperty("code").GetInt32());

        // 设成直接子 B → 同样成环
        var underChild = await Update(admin, a, b, "A");
        Assert.Equal((int)ErrorCode.OrgInvalidParent, underChild.GetProperty("code").GetInt32());

        // 设成自己 → 老规矩,依旧拒
        var self = await Update(admin, a, a, "A");
        Assert.Equal((int)ErrorCode.OrgInvalidParent, self.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Reparenting_to_an_unrelated_org_still_works()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var a = await AddOrg(admin, "A", 0);
        var b = await AddOrg(admin, "B", a);   // B 在 A 下
        var x = await AddOrg(admin, "X", 0);   // X 是独立根

        // 把 B 挪到 X 下(X 不是 B 的后代)→ 允许
        var ok = await Update(admin, b, x, "B");
        Assert.Equal(0, ok.GetProperty("code").GetInt32());
    }
}
