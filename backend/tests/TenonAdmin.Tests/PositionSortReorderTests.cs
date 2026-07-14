using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TenonAdmin.Tests;

/// <summary>
/// 分页安全排序(SortField/SortOrder)+ 行拖拽重排的 HTTP 级回归。锁三条:
/// 合法字段客户端排序生效;非法/注入字段被忽略、回退默认 Sort 排序(不报错、防注入);reorder 按序重排 Sort。
/// </summary>
public class PositionSortReorderTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<long> AddPosition(HttpClient c, string name, string code, int sort) =>
        (await (await c.PostJson("/api/v1/sys/position/add", new { name, code, sort, enabled = true })).ReadEnvelope())
            .GetProperty("data").GetInt64();

    /// <summary>按 Name 前缀分页,返回 items 里该前缀的名称序列(用于断言顺序)。</summary>
    private static async Task<List<string>> PageNames(HttpClient c, string query)
    {
        var data = (await (await c.GetAsync($"/api/v1/sys/position/page?{query}")).ReadEnvelope()).GetProperty("data");
        return data.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToList();
    }

    [Fact]
    public async Task ClientSort_valid_field_desc_takes_effect_and_injection_is_ignored()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        await AddPosition(c, "ZSORT_C", "ZSORT_C", 3);
        await AddPosition(c, "ZSORT_A", "ZSORT_A", 1);
        await AddPosition(c, "ZSORT_B", "ZSORT_B", 2);

        // 合法字段 name 降序 → C,B,A
        var desc = await PageNames(c, "Name=ZSORT&SortField=name&SortOrder=desc&Size=50");
        Assert.Equal(new[] { "ZSORT_C", "ZSORT_B", "ZSORT_A" }, desc);

        // 非法/注入字段 → 忽略、回退默认 Sort 升序(A=1,B=2,C=3),且不报错(防注入红线)
        var injected = Uri.EscapeDataString("name); DROP TABLE sys_position;--");
        var resp = await c.GetAsync($"/api/v1/sys/position/page?Name=ZSORT&SortField={injected}&Size=50");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var names = (await resp.ReadEnvelope()).GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToList();
        Assert.Equal(new[] { "ZSORT_A", "ZSORT_B", "ZSORT_C" }, names);
    }

}
