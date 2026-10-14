using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>发布时必须有审批节点；有表单时条件只能引用表单字段。</summary>
public class WfPublishStructureTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Publish_requires_an_approval_node()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertReason(admin, "只有发起", StartOnly(), "approvalRequired", nodeId: null);
        await AssertReason(admin, "只有抄送", CcOnly(), "approvalRequired", nodeId: null);
    }

    [Fact]
    public async Task Condition_fields_must_exist_on_the_form()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertReason(
            admin,
            "条件字段不存在",
            BranchModel(fields: ["reason"], conditionField: "amount"),
            "conditionFieldUnknown",
            "branch1");

        await AssertReason(
            admin,
            "嵌套条件字段不存在",
            BranchModel(fields: ["reason"], conditionField: "reason", nestedField: "amount"),
            "conditionFieldUnknown",
            "branch1");
        await AssertReason(
            admin,
            "条件字段大小写不一致",
            BranchModel(fields: ["amount"], conditionField: "Amount"),
            "conditionFieldUnknown",
            "branch1");

        var known = await Publish(admin, "条件字段存在", BranchModel(fields: ["amount"], conditionField: "amount"));
        Assert.True(known > 0);

        var legacy = await Publish(admin, "无表单自由变量", BranchModel(fields: null, conditionField: "amount"));
        Assert.True(legacy > 0);

        var component = await Publish(admin, "挂载点自由变量", BranchModel(fields: null, conditionField: "amount", formComponent: "views/biz/leave/form"));
        Assert.True(component > 0);
    }

    private static object StartOnly() => new
    {
        version = 1,
        root = new { id = "start", type = "start", name = "发起" },
    };

    private static object CcOnly() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "cc1",
                type = "cc",
                name = "抄送",
                props = new { assignee = new { provider = "initiator", @params = new { } } },
            },
        },
    };

    private static object BranchModel(string[]? fields, string conditionField, string? nestedField = null, string? formComponent = null)
    {
        object expr = nestedField is null
            ? new { field = conditionField, op = "gt", value = 1 }
            : new
            {
                logic = "and",
                children = new object[]
                {
                    new { field = conditionField, op = "eq", value = "ok" },
                    new { field = nestedField, op = "gt", value = 1 },
                },
            };
        return new
        {
            version = 1,
            formComponent,
            formSchema = fields is null ? null : new
            {
                version = 1,
                fields = fields.Select(key => new { key, label = key, type = "text", required = false }).ToArray(),
            },
            root = new
            {
                id = "start",
                type = "start",
                name = "发起",
                next = new
                {
                    id = "branch1",
                    type = "branch",
                    name = "分支",
                    conditions = new object[]
                    {
                        new { id = "arm", name = "条件", isDefault = false, expr },
                        new { id = "fallback", name = "默认", isDefault = true },
                    },
                    next = new
                    {
                        id = "approval",
                        type = "approval",
                        name = "审批",
                        props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                    },
                },
            },
        };
    }

    private static async Task AssertReason(HttpClient admin, string name, object model, string reason, string? nodeId)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = added.GetProperty("data").GetInt64() });
        Assert.Equal(WorkflowErrorCode.ModelInvalid, published.GetProperty("code").GetInt32());
        Assert.Equal(reason, published.GetProperty("args").GetProperty("reason").GetString());
        if (nodeId is null)
            Assert.False(published.GetProperty("args").TryGetProperty("nodeId", out _));
        else
            Assert.Equal(nodeId, published.GetProperty("args").GetProperty("nodeId").GetString());
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
