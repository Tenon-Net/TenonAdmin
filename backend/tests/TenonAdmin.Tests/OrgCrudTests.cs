using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 机构增删改查的 HTTP 级基础回归:显式/自动编码、编码查重、改名、有子机构不可删、
/// 删叶子机构后从列表消失、查不存在的机构返回 OrgNotFound。环检测见 OrgCycleTests,复制见 OrgCopyTests。
/// </summary>
public class OrgCrudTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<JsonElement> Add(HttpClient c, long parentId, string name, string code) =>
        await (await c.PostJson("/api/v1/sys/org/add",
            new { parentId, name, code, category = "", sort = 0, enabled = true })).ReadEnvelope();

    private static async Task<long> AddOrg(HttpClient c, string name, long parentId = 0, string code = "") =>
        (await Add(c, parentId, name, code)).GetProperty("data").GetInt64();

    private static async Task<JsonElement> Get(HttpClient c, long id) =>
        await (await c.GetAsync($"/api/v1/sys/org/{id}")).ReadEnvelope();

    private static async Task<JsonElement> OrgList(HttpClient c) =>
        (await (await c.GetAsync("/api/v1/sys/org/list")).ReadEnvelope()).GetProperty("data");

    [Fact]
    public async Task Add_with_explicit_code_persists_it_verbatim()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var added = await Add(admin, 0, "显式编码机构", "EXPLICIT_CODE_1");
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();

        var got = await Get(admin, id);
        Assert.Equal(0, got.GetProperty("code").GetInt32());
        Assert.Equal("EXPLICIT_CODE_1", got.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Add_with_empty_code_auto_generates_a_nonempty_code()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var added = await Add(admin, 0, "自动编码机构", "");
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();

        var got = await Get(admin, id);
        Assert.Equal(0, got.GetProperty("code").GetInt32());
        var generatedCode = got.GetProperty("data").GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(generatedCode));
    }

    [Fact]
    public async Task Add_with_duplicate_explicit_code_returns_OrgCodeExists()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var first = await Add(admin, 0, "编码占用者", "DUP_CODE_1");
        Assert.Equal(0, first.GetProperty("code").GetInt32());

        var second = await Add(admin, 0, "编码冲突者", "DUP_CODE_1");
        Assert.Equal((int)ErrorCode.OrgCodeExists, second.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Update_changes_the_org_name()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var id = await AddOrg(admin, "改名前", code: "RENAME_ORG_1");

        var updated = await (await admin.PutJson($"/api/v1/sys/org/{id}",
            new { parentId = 0, name = "改名后", code = "RENAME_ORG_1", category = "", sort = 0, enabled = true }))
            .ReadEnvelope();
        Assert.Equal(0, updated.GetProperty("code").GetInt32());

        var got = await Get(admin, id);
        Assert.Equal("改名后", got.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Delete_org_with_children_returns_OrgHasChildren()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var parent = await AddOrg(admin, "有子机构的父", code: "PARENT_WITH_CHILD");
        await AddOrg(admin, "子机构", parentId: parent, code: "CHILD_OF_PARENT");

        var deleted = await (await admin.DeleteAsync($"/api/v1/sys/org/{parent}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.OrgHasChildren, deleted.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Delete_leaf_org_succeeds_and_disappears_from_list()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var id = await AddOrg(admin, "待删除叶子机构", code: "LEAF_TO_DELETE");

        var deleted = await (await admin.DeleteAsync($"/api/v1/sys/org/{id}")).ReadEnvelope();
        Assert.Equal(0, deleted.GetProperty("code").GetInt32());

        var list = await OrgList(admin);
        Assert.DoesNotContain(list.EnumerateArray(), o => o.GetProperty("id").GetInt64() == id);
    }

    [Fact]
    public async Task Get_nonexistent_org_returns_OrgNotFound()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var got = await Get(admin, 999999999);
        Assert.Equal((int)ErrorCode.OrgNotFound, got.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Leader_must_exist_and_be_enabled()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var missing = await (await admin.PostJson("/api/v1/sys/org/add", new
        {
            parentId = 0,
            name = "无效负责人",
            code = "BAD_LEADER_MISSING",
            category = "",
            sort = 0,
            enabled = true,
            leaderUserId = 9_999_999L,
        })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.UserNotFound, missing.GetProperty("code").GetInt32());

        var disabledUser = await (await admin.PostJson("/api/v1/sys/user", new
        {
            account = "disabled-org-leader",
            password = "Test@123456",
            name = "停用负责人",
            enabled = false,
            orgId = 1,
            roleIds = Array.Empty<long>(),
        })).ReadEnvelope();
        var disabledId = disabledUser.GetProperty("data").GetProperty("id").GetInt64();
        var disabled = await (await admin.PostJson("/api/v1/sys/org/add", new
        {
            parentId = 0,
            name = "停用负责人机构",
            code = "BAD_LEADER_DISABLED",
            category = "",
            sort = 0,
            enabled = true,
            leaderUserId = disabledId,
        })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.AccountDisabled, disabled.GetProperty("code").GetInt32());
    }
}
