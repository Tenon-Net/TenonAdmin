using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 内置字典模块(类型 + 项)的增删改查回归。默认 <see cref="AdminAppFactory"/> 关闭 Dict 模块
/// (让 TestHost 的自定义字典控制器接管其路由),因此这里的每个用例都显式以
/// <c>DisabledModules = []</c> 构造工厂,才能命中 <c>TenonAdmin.AspNetCore.DictController</c>。
/// </summary>
public class DictCrudTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Create_type_is_retrievable_by_id()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "biz_status", name = "业务状态", sort = 1, enabled = true, remark = "测试类型" })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var typeId = add.GetProperty("data").GetInt64();
        Assert.True(typeId > 0);

        // 用 GET type/{id} 回读而非 type/page:本测试宿主(TestHost)的 CustomDictController 也映射了 type/page
        // (可替换性用例的固定装置),两个控制器同 DisabledModules=[] 时 type/page 路由歧义 → 500;type/{id} 只在内置控制器上,无歧义。
        var get = await (await admin.GetAsync($"/api/v1/sys/dict/type/{typeId}")).ReadEnvelope();
        Assert.Equal(0, get.GetProperty("code").GetInt32());
        Assert.Equal("biz_status", get.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal("业务状态", get.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Create_item_under_type()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        var addType = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "order_state", name = "订单状态", sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal(0, addType.GetProperty("code").GetInt32());

        var addItem = await (await admin.PostJson("/api/v1/sys/dict/item",
            new { dictTypeCode = "order_state", label = "待支付", value = "0", sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal(0, addItem.GetProperty("code").GetInt32());
        Assert.True(addItem.GetProperty("data").GetInt64() > 0);
    }

    [Fact]
    public async Task Items_by_type_returns_enabled_items()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "pay_channel", name = "支付渠道", sort = 1, enabled = true });
        await admin.PostJson("/api/v1/sys/dict/item",
            new { dictTypeCode = "pay_channel", label = "微信", value = "wx", sort = 1, enabled = true });
        await admin.PostJson("/api/v1/sys/dict/item",
            new { dictTypeCode = "pay_channel", label = "支付宝", value = "alipay", sort = 2, enabled = true });

        var envelope = await (await admin.GetAsync("/api/v1/sys/dict/items/pay_channel")).ReadEnvelope();
        Assert.Equal(0, envelope.GetProperty("code").GetInt32());
        var list = envelope.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, i => i.GetProperty("value").GetString() == "wx");
        Assert.Contains(list, i => i.GetProperty("value").GetString() == "alipay");
    }

    [Fact]
    public async Task Items_by_type_empty_when_type_disabled()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "off_channel", name = "停用渠道", sort = 1, enabled = true })).ReadEnvelope();
        var typeId = add.GetProperty("data").GetInt64();
        await admin.PostJson("/api/v1/sys/dict/item",
            new { dictTypeCode = "off_channel", label = "微信", value = "wx", sort = 1, enabled = true });

        Assert.Single((await (await admin.GetAsync("/api/v1/sys/dict/items/off_channel")).ReadEnvelope())
            .GetProperty("data").EnumerateArray());

        await admin.PutJson($"/api/v1/sys/dict/type/{typeId}",
            new { code = "off_channel", name = "停用渠道", sort = 1, enabled = false });

        var after = await (await admin.GetAsync("/api/v1/sys/dict/items/off_channel")).ReadEnvelope();
        Assert.Equal(0, after.GetProperty("code").GetInt32());
        Assert.Empty(after.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Update_type_changes_name_but_code_stays_immutable()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        // 用非种子编码(种子已播 common_status / org_category / gender,撞了会被唯一约束拒、AddType 返回错误信封)
        var add = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "biz_grade", name = "等级", sort = 1, enabled = true })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var typeId = add.GetProperty("data").GetInt64();

        // 服务层显式忽略 Code 更新(DictService.UpdateTypeAsync 只改 Name/Sort/Enabled/Remark),
        // 这里故意传一个不同的 code,验证它被无声丢弃、不是报错。
        var update = await (await admin.PutJson($"/api/v1/sys/dict/type/{typeId}",
            new { code = "biz_grade-changed", name = "等级(已更新)", sort = 2, enabled = false, remark = "更新备注" })).ReadEnvelope();
        Assert.Equal(0, update.GetProperty("code").GetInt32());

        var get = await (await admin.GetAsync($"/api/v1/sys/dict/type/{typeId}")).ReadEnvelope();
        Assert.Equal(0, get.GetProperty("code").GetInt32());
        var data = get.GetProperty("data");
        Assert.Equal("biz_grade", data.GetProperty("code").GetString());           // Code 未变
        Assert.Equal("等级(已更新)", data.GetProperty("name").GetString());          // Name 已变
        Assert.False(data.GetProperty("enabled").GetBoolean());                    // Enabled 已变
    }

    [Fact]
    public async Task Delete_type_cascades_its_items()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        var add = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "cascade_type", name = "级联测试类型", sort = 1, enabled = true })).ReadEnvelope();
        var typeId = add.GetProperty("data").GetInt64();
        await admin.PostJson("/api/v1/sys/dict/item",
            new { dictTypeCode = "cascade_type", label = "项A", value = "a", sort = 1, enabled = true });

        var beforeDelete = await (await admin.GetAsync("/api/v1/sys/dict/items/cascade_type")).ReadEnvelope();
        Assert.Single(beforeDelete.GetProperty("data").EnumerateArray());

        var delete = await (await admin.DeleteAsync($"/api/v1/sys/dict/type/{typeId}")).ReadEnvelope();
        Assert.Equal(0, delete.GetProperty("code").GetInt32());

        var afterDelete = await (await admin.GetAsync("/api/v1/sys/dict/items/cascade_type")).ReadEnvelope();
        Assert.Empty(afterDelete.GetProperty("data").EnumerateArray());

        // 类型本身也已不存在:GetTypeAsync 找不到时抛 ErrorCode.DictTypeNotFound (43001)
        var getDeletedType = await (await admin.GetAsync($"/api/v1/sys/dict/type/{typeId}")).ReadEnvelope();
        Assert.Equal(43001, getDeletedType.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_delete_types_removes_all()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        var add1 = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "batch_t1", name = "批删类型1", sort = 1, enabled = true })).ReadEnvelope();
        var add2 = await (await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "batch_t2", name = "批删类型2", sort = 2, enabled = true })).ReadEnvelope();
        var id1 = add1.GetProperty("data").GetInt64();
        var id2 = add2.GetProperty("data").GetInt64();

        var batchDelete = await (await admin.PostJson("/api/v1/sys/dict/type/batch-delete",
            new { ids = new[] { id1, id2 } })).ReadEnvelope();
        Assert.Equal(0, batchDelete.GetProperty("code").GetInt32());

        var get1 = await (await admin.GetAsync($"/api/v1/sys/dict/type/{id1}")).ReadEnvelope();
        var get2 = await (await admin.GetAsync($"/api/v1/sys/dict/type/{id2}")).ReadEnvelope();
        Assert.Equal(43001, get1.GetProperty("code").GetInt32());
        Assert.Equal(43001, get2.GetProperty("code").GetInt32());
    }

    // ── QA13: Seed data protection ──────────────────────────────────────

    [Fact]
    public async Task Delete_seed_dict_type_returns_SeedDataProtected()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        // Id=1 is the seed dict type "common_status"
        var env = await (await admin.DeleteAsync("/api/v1/sys/dict/type/1")).ReadEnvelope();
        Assert.Equal(43010, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Delete_seed_dict_item_returns_SeedDataProtected()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        // Id=1 is the seed dict item under "common_status"
        var env = await (await admin.DeleteAsync("/api/v1/sys/dict/item/1")).ReadEnvelope();
        Assert.Equal(43010, env.GetProperty("code").GetInt32());
    }

    // ── QA13: Dict item value uniqueness ────────────────────────────────

    [Fact]
    public async Task Add_duplicate_dict_item_value_returns_DictItemValueExists()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var admin = await SuperAdminClient(f);

        await admin.PostJson("/api/v1/sys/dict/type",
            new { code = "dup_val_test", name = "值唯一测试", sort = 1, enabled = true });
        await admin.PostJson("/api/v1/sys/dict/item",
            new { dictTypeCode = "dup_val_test", label = "标签A", value = "same_val", sort = 1, enabled = true });

        var dup = await (await admin.PostJson("/api/v1/sys/dict/item",
            new { dictTypeCode = "dup_val_test", label = "标签B", value = "same_val", sort = 2, enabled = true })).ReadEnvelope();
        Assert.Equal(43011, dup.GetProperty("code").GetInt32());
    }
}
