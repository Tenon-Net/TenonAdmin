using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 可授权路由清单(D1):把"权限码"从管理员手敲的魔法字符串,变成从后端真实路由表里选。
/// <para>关键不变量:① 清单里的码与授权管道算出的码<b>一字不差</b>(否则选了也匹配不上);
/// ② <b>消费方自建控制器自动出现</b>(内核的核心卖点是消费者加自己的业务模块);
/// ③ 只列受权端点(没挂 [RolePermission] 的匿名/仅登录端点不参与角色授权,列出来是误导)。</para>
/// </summary>
public class PermissionRoutesEndpointTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<List<string>> Codes(HttpClient admin)
    {
        var j = await (await admin.GetAsync("/api/v1/sys/menu/routes")).ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        return j.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("code").GetString()!).ToList();
    }

    [Fact]
    public async Task Lists_built_in_and_consumer_endpoints_with_exact_permission_codes()
    {
        using var f = new AdminAppFactory();
        var codes = await Codes(await SuperAdminClient(f));

        // 内置端点:码的形状必须与种子/授权管道完全一致(小写路由 + {id} 占位符原样保留)
        Assert.Contains("GET:/api/v1/sys/user/page", codes);
        Assert.Contains("PUT:/api/v1/sys/user/{id}/password", codes);
        Assert.Contains("PUT:/api/v1/sys/role/datascope", codes);
        Assert.Contains("GET:/api/v1/sys/menu/routes", codes);   // 自己也在清单里

        // 消费方自建控制器(TestHost 的 SampleDoc)——不自动出现,这个接口就没有意义
        Assert.Contains(codes, c => c.Contains("/api/v1/sample/doc"));
    }

    [Fact]
    public async Task Excludes_anonymous_and_active_session_only_endpoints()
    {
        using var f = new AdminAppFactory();
        var codes = await Codes(await SuperAdminClient(f));

        Assert.DoesNotContain("POST:/api/v1/auth/login", codes);          // [AllowAnonymous]
        Assert.DoesNotContain("GET:/api/v1/personal/profile", codes);     // [ActiveSession](任何登录用户,无需授权)
    }

    /// <summary>清单里的每个码,都必须能真的授权成功——即与 RolePermissionAttribute 的比对逻辑同源。</summary>
    [Fact]
    public async Task Codes_from_the_list_actually_grant_access()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 建角色 + 建用户,只授"用户分页"这一个码(直接取自路由清单,不手抄)
        var codes = await Codes(admin);
        var target = codes.First(c => c == "GET:/api/v1/sys/user/page");

        var roleId = (await (await admin.PostJson("/api/v1/sys/role/add",
            new { name = "只读", code = "readonly-user", sort = 0, enabled = true })).ReadEnvelope())
            .GetProperty("data").GetInt64();

        // 找到该权限码对应的菜单节点(种子里有),授给角色
        var tree = await (await admin.GetAsync("/api/v1/sys/menu/tree")).ReadEnvelope();
        var menuId = FindByPermission(tree.GetProperty("data"), target);
        Assert.NotNull(menuId);
        await admin.PutJson("/api/v1/sys/role/menu", new { roleId, menuIds = new[] { menuId!.Value } });

        await admin.PostJson("/api/v1/sys/user",
            new { account = "readonly", password = "InitPass123", name = "只读用户", enabled = true, roleIds = new[] { roleId } });

        var anon = f.CreateClient();
        var token = (await (await anon.PostJson("/api/v1/auth/login",
            new { account = "readonly", password = "InitPass123" })).ReadEnvelope())
            .GetProperty("data").GetProperty("accessToken").GetString()!;
        var user = f.CreateClient();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 授了的码 → 200;没授的码 → 403(证明清单里的码就是授权管道比对的那个码)
        Assert.Equal(System.Net.HttpStatusCode.OK, (await user.GetAsync("/api/v1/sys/user/page?Current=1&Size=10")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/sys/role/page?Current=1&Size=10")).StatusCode);
    }

    /// <summary>在菜单树里按权限码找节点 Id(深度优先)。</summary>
    private static long? FindByPermission(System.Text.Json.JsonElement nodes, string permission)
    {
        foreach (var n in nodes.EnumerateArray())
        {
            if (n.TryGetProperty("permission", out var p) && p.GetString() == permission)
                return n.GetProperty("id").GetInt64();
            if (n.TryGetProperty("children", out var children) && children.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var found = FindByPermission(children, permission);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
