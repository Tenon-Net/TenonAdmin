using System.Net.Http.Headers;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 用户管理基础 CRUD + 账号唯一 + 超管护栏(设计 §4)。
/// 密码相关安全细节见 <c>PasswordResetSecurityTests</c>/<c>ForcedPasswordChangeTests</c>,
/// 角色反查分页见 <c>UserRoleFilterTests</c>,资料扩展字段见 <c>UserProfileFieldsTests</c> —— 此处不重复覆盖。
/// </summary>
public class UserCrudTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Add_user_then_get_detail_returns_matching_account()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "crud-add", password = "Test@123456", name = "Add Me", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var data = add.GetProperty("data");
        var id = data.GetProperty("id").GetInt64();
        Assert.True(id > 0);
        Assert.Equal("Test@123456", data.GetProperty("initialPassword").GetString());   // 显式指定口令 → 原样回传

        var detail = await (await admin.GetAsync($"/api/v1/sys/user/{id}")).ReadEnvelope();
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        Assert.Equal("crud-add", detail.GetProperty("data").GetProperty("account").GetString());
    }

    [Fact]
    public async Task Add_user_with_existing_account_returns_AccountExists()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        await (await admin.PostJson("/api/v1/sys/user",
            new { account = "dup-account", password = "Test@123456", name = "First", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();

        var second = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "dup-account", password = "Test@123456", name = "Second", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();

        Assert.Equal((int)ErrorCode.AccountExists, second.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Update_user_changes_name()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "crud-update", password = "Test@123456", name = "Old Name", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        var id = add.GetProperty("data").GetProperty("id").GetInt64();

        var update = await (await admin.PutJson($"/api/v1/sys/user/{id}",
            new { name = "New Name", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        Assert.Equal(0, update.GetProperty("code").GetInt32());

        var detail = await (await admin.GetAsync($"/api/v1/sys/user/{id}")).ReadEnvelope();
        Assert.Equal("New Name", detail.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Delete_user_removes_it_from_page()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "crud-delete", password = "Test@123456", name = "Delete Me", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        var id = add.GetProperty("data").GetProperty("id").GetInt64();

        var delete = await (await admin.DeleteAsync($"/api/v1/sys/user/{id}")).ReadEnvelope();
        Assert.Equal(0, delete.GetProperty("code").GetInt32());

        var page = await (await admin.GetAsync("/api/v1/sys/user/page?Current=1&Size=100")).ReadEnvelope();
        var accounts = page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("account").GetString()).ToList();
        Assert.DoesNotContain("crud-delete", accounts);
    }

    [Fact]
    public async Task Delete_super_admin_is_protected()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var delete = await (await admin.DeleteAsync("/api/v1/sys/user/1")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.SuperAdminProtected, delete.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_delete_containing_super_admin_rejects_whole_batch()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "crud-batch", password = "Test@123456", name = "Batch Me", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        var id = add.GetProperty("data").GetProperty("id").GetInt64();

        var batch = await (await admin.PostJson("/api/v1/sys/user/batch-delete",
            new { ids = new[] { id, 1L } })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.SuperAdminProtected, batch.GetProperty("code").GetInt32());

        // 整批拒绝:随批的普通用户也不该被删掉(不是"删其余、跳超管"的部分成功)
        var page = await (await admin.GetAsync("/api/v1/sys/user/page?Current=1&Size=100")).ReadEnvelope();
        var accounts = page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("account").GetString()).ToList();
        Assert.Contains("crud-batch", accounts);
    }

    [Fact]
    public async Task Set_enabled_disable_then_enable_flips_flag_on_normal_user()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "crud-toggle", password = "Test@123456", name = "Toggle Me", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        var id = add.GetProperty("data").GetProperty("id").GetInt64();

        var disable = await (await admin.PutJson($"/api/v1/sys/user/{id}/enabled", new { enabled = false })).ReadEnvelope();
        Assert.Equal(0, disable.GetProperty("code").GetInt32());
        var afterDisable = await (await admin.GetAsync($"/api/v1/sys/user/{id}")).ReadEnvelope();
        Assert.False(afterDisable.GetProperty("data").GetProperty("enabled").GetBoolean());

        var enable = await (await admin.PutJson($"/api/v1/sys/user/{id}/enabled", new { enabled = true })).ReadEnvelope();
        Assert.Equal(0, enable.GetProperty("code").GetInt32());
        var afterEnable = await (await admin.GetAsync($"/api/v1/sys/user/{id}")).ReadEnvelope();
        Assert.True(afterEnable.GetProperty("data").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Set_enabled_disable_on_super_admin_is_protected()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var disable = await (await admin.PutJson("/api/v1/sys/user/1/enabled", new { enabled = false })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.SuperAdminProtected, disable.GetProperty("code").GetInt32());
    }
}
