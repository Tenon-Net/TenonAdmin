using System.Net.Http.Headers;
using System.Text.Json;

namespace TenonAdmin.Tests;

/// <summary>
/// 菜单管理 CRUD 的 HTTP 级回归(设计 §16)。业务失败走统一信封 HTTP 200 + 业务码
/// (见 AdminExceptionFilter),故断言落在信封 code 上。重点锁两条不变量:
/// 删除带子节点拒绝、ModuleId 仅顶级目录保留(子节点强制置空)。
/// </summary>
public class MenuCrudTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>在菜单树(嵌套)里按 Id 递归查节点。</summary>
    private static JsonElement? Find(JsonElement nodes, long id)
    {
        foreach (var n in nodes.EnumerateArray())
        {
            if (n.GetProperty("id").GetInt64() == id) return n;
            var hit = Find(n.GetProperty("children"), id);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static async Task<JsonElement> TreeData(HttpClient c) =>
        (await (await c.GetAsync("/api/v1/sys/menu/tree")).ReadEnvelope()).GetProperty("data");

    private static async Task<long> AddMenu(HttpClient c, object body) =>
        (await (await c.PostJson("/api/v1/sys/menu/add", body)).ReadEnvelope()).GetProperty("data").GetInt64();

    [Fact]
    public async Task SuperAdmin_can_crud_menu()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 新增顶级目录
        var catId = await AddMenu(c, new { parentId = 0, type = 1, title = "测试目录", permission = "", sort = 90, enabled = true, visible = true });

        // 树里能查到
        Assert.NotNull(Find(await TreeData(c), catId));

        // 目录下加页面节点
        var pageId = await AddMenu(c, new { parentId = catId, type = 2, title = "测试页面", permission = "GET:/api/v1/test", sort = 1, enabled = true, path = "/test", component = "test/index", visible = true });

        // 更新页面标题
        var upd = await c.PutJson($"/api/v1/sys/menu/{pageId}", new { parentId = catId, type = 2, title = "测试页面V2", permission = "GET:/api/v1/test", sort = 1, enabled = true, path = "/test", component = "test/index", visible = true });
        Assert.Equal(0, (await upd.ReadEnvelope()).GetProperty("code").GetInt32());
        var page = Find(await TreeData(c), pageId)!.Value;
        Assert.Equal("测试页面V2", page.GetProperty("title").GetString());

        // 删除叶子页面 → 目录变叶子 → 删目录
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/menu/{pageId}")).ReadEnvelope()).GetProperty("code").GetInt32());
        Assert.Equal(0, (await (await c.DeleteAsync($"/api/v1/sys/menu/{catId}")).ReadEnvelope()).GetProperty("code").GetInt32());
        Assert.Null(Find(await TreeData(c), catId));
    }

    [Fact]
    public async Task Delete_menu_with_children_is_rejected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var parent = await AddMenu(c, new { parentId = 0, type = 1, title = "有子目录", permission = "", sort = 91, enabled = true, visible = true });
        await AddMenu(c, new { parentId = parent, type = 2, title = "子页面", permission = "", sort = 1, enabled = true, visible = true });

        var del = await (await c.DeleteAsync($"/api/v1/sys/menu/{parent}")).ReadEnvelope();
        Assert.Equal(42016, del.GetProperty("code").GetInt32());   // MenuHasChildren
    }

    [Fact]
    public async Task ModuleId_is_kept_only_on_top_level()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 顶级目录设 ModuleId=1(内置 system 模块)→ 保留
        var top = await AddMenu(c, new { parentId = 0, type = 1, title = "顶级挂模块", permission = "", sort = 92, enabled = true, moduleId = 1, visible = true });
        // 子节点即便传 ModuleId=1 → 服务端强制置空(归属靠上溯解析)
        var child = await AddMenu(c, new { parentId = top, type = 2, title = "子节点传模块", permission = "", sort = 1, enabled = true, moduleId = 1, visible = true });

        var data = await TreeData(c);
        Assert.Equal(1, Find(data, top)!.Value.GetProperty("moduleId").GetInt64());
        Assert.Equal(JsonValueKind.Null, Find(data, child)!.Value.GetProperty("moduleId").ValueKind);
    }

    [Fact]
    public async Task Update_nonexistent_menu_returns_not_found()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var upd = await c.PutJson("/api/v1/sys/menu/999999999", new { parentId = 0, type = 1, title = "X", permission = "", sort = 0, enabled = true, visible = true });
        Assert.Equal(42015, (await upd.ReadEnvelope()).GetProperty("code").GetInt32());   // MenuNotFound
    }

    [Fact]
    public async Task Delete_nonexistent_menu_returns_not_found()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var del = await (await c.DeleteAsync("/api/v1/sys/menu/999999999")).ReadEnvelope();
        Assert.Equal(42015, del.GetProperty("code").GetInt32());   // MenuNotFound
    }

    [Fact]
    public async Task Create_under_nonexistent_parent_is_rejected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var add = await c.PostJson("/api/v1/sys/menu/add", new { parentId = 999999999, type = 2, title = "孤儿", permission = "", sort = 1, enabled = true, visible = true });
        Assert.Equal(42017, (await add.ReadEnvelope()).GetProperty("code").GetInt32());   // MenuInvalidParent
    }

    [Fact]
    public async Task Reparent_under_self_or_descendant_is_rejected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var a = await AddMenu(c, new { parentId = 0, type = 1, title = "A", permission = "", sort = 93, enabled = true, visible = true });
        var b = await AddMenu(c, new { parentId = a, type = 1, title = "B", permission = "", sort = 1, enabled = true, visible = true });

        // 指向自身 → 拒绝
        var self = await c.PutJson($"/api/v1/sys/menu/{a}", new { parentId = a, type = 1, title = "A", permission = "", sort = 93, enabled = true, visible = true });
        Assert.Equal(42017, (await self.ReadEnvelope()).GetProperty("code").GetInt32());

        // 指向自身子孙(A 挂到其子 B 下)→ 拒绝(否则子树成环从树上消失)
        var cycle = await c.PutJson($"/api/v1/sys/menu/{a}", new { parentId = b, type = 1, title = "A", permission = "", sort = 93, enabled = true, visible = true });
        Assert.Equal(42017, (await cycle.ReadEnvelope()).GetProperty("code").GetInt32());

        // A 仍在树上(未被破坏)
        Assert.NotNull(Find(await TreeData(c), a));
    }

    [Fact]
    public async Task ModuleId_invariant_holds_on_update()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var top = await AddMenu(c, new { parentId = 0, type = 1, title = "顶级", permission = "", sort = 94, enabled = true, moduleId = 1, visible = true });
        var other = await AddMenu(c, new { parentId = 0, type = 1, title = "另一顶级", permission = "", sort = 95, enabled = true, visible = true });

        // 把带模块的顶级移到别的顶级下 → ModuleId 应被置空(不再是顶级)
        await c.PutJson($"/api/v1/sys/menu/{top}", new { parentId = other, type = 1, title = "顶级", permission = "", sort = 94, enabled = true, moduleId = 1, visible = true });
        Assert.Equal(JsonValueKind.Null, Find(await TreeData(c), top)!.Value.GetProperty("moduleId").ValueKind);

        // 再提回顶级并带模块 → ModuleId 应保留
        await c.PutJson($"/api/v1/sys/menu/{top}", new { parentId = 0, type = 1, title = "顶级", permission = "", sort = 94, enabled = true, moduleId = 1, visible = true });
        Assert.Equal(1, Find(await TreeData(c), top)!.Value.GetProperty("moduleId").GetInt64());
    }
}
