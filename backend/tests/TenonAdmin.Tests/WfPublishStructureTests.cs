using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>空流程不能发布；审批、Webhook、AI 决策可以。有表单时条件只能引用表单字段。</summary>
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
    public async Task Webhook_only_flow_publishes_and_starts_an_execution()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var definitionId = await Publish(admin, "纯 Webhook", WebhookOnly());

        var started = await PostEnvelope(admin, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        Assert.Equal((int)WfInstanceStatus.Running, started.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Contains("webhook", detail.GetProperty("data").GetProperty("currentNodeIds").EnumerateArray().Select(node => node.GetString()));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var execution = await db.Queryable<WfNodeExecution>().Where(row => row.InstanceId == instanceId).FirstAsync();
        Assert.Equal(WfNodeType.Webhook, execution.NodeType);
        Assert.Equal(WfNodeExecutionStatus.Pending, execution.Status);
    }

    [Fact]
    public async Task Webhook_inside_a_branch_arm_can_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var definitionId = await Publish(admin, "分支内 Webhook", WebhookInBranch());
        Assert.True(definitionId > 0);
    }

    [Fact]
    public async Task Ai_decision_flow_publishes_with_manual_fallback_and_stays_running()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var definitionId = await Publish(admin, "纯 AI 决策", AiDecisionOnly());

        var started = await PostEnvelope(admin, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            variablesJson = """{"amount":12}""",
        });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var data = started.GetProperty("data");
        var instanceId = data.GetProperty("instanceId").GetInt64();
        Assert.Equal((int)WfInstanceStatus.Running, data.GetProperty("instanceStatus").GetInt32());

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        var ai = detail.GetProperty("data").GetProperty("model").GetProperty("root").GetProperty("next");
        Assert.Equal("aiDecision", ai.GetProperty("type").GetString());
        Assert.Equal("user", ai.GetProperty("props").GetProperty("assignee").GetProperty("provider").GetString());
        Assert.False(ai.GetProperty("props").TryGetProperty("aiInstructions", out _));
        Assert.False(ai.GetProperty("props").TryGetProperty("aiInputFields", out _));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var published = await db.Queryable<WfDefinitionVersion>()
            .Where(version => version.DefinitionId == definitionId && version.Version >= 1)
            .FirstAsync();
        using var snapshot = JsonDocument.Parse(published.ModelJson!);
        var stored = snapshot.RootElement.GetProperty("root").GetProperty("next");
        Assert.Equal("Review the selected case.", stored.GetProperty("props").GetProperty("aiInstructions").GetString());
        Assert.Equal("amount", stored.GetProperty("props").GetProperty("aiInputFields")[0].GetString());
        Assert.Equal(1, stored.GetProperty("props").GetProperty("assignee").GetProperty("params").GetProperty("userIds")[0].GetInt64());

        var execution = await db.Queryable<WfNodeExecution>().Where(row => row.InstanceId == instanceId).FirstAsync();
        Assert.Equal(WfNodeType.AiDecision, execution.NodeType);
        Assert.Equal(WfNodeExecutionStatus.Pending, execution.Status);
        Assert.Equal(0, await db.Queryable<WfTask>().Where(task => task.InstanceId == instanceId).CountAsync());
    }

    [Fact]
    public async Task Ordinary_approval_flow_still_publishes()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var definitionId = await Publish(admin, "普通审批", new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "发起",
                next = new
                {
                    id = "approval",
                    type = "approval",
                    name = "审批",
                    props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                },
            },
        });
        Assert.True(definitionId > 0);
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

    [Fact]
    public async Task Condition_leaves_must_name_a_field()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertReason(admin, "条件字段为空", BranchModel(fields: null, conditionField: ""),
            "conditionFieldRequired", "branch1");
        await AssertReason(admin, "条件字段为空白", BranchModel(fields: null, conditionField: "  "),
            "conditionFieldRequired", "branch1");
        await AssertReason(admin, "条件字段缺失", BranchModel(fields: null, conditionField: null),
            "conditionFieldRequired", "branch1");
        await AssertReason(admin, "嵌套条件字段为空", BranchModel(fields: ["amount"], conditionField: "amount", nestedField: ""),
            "conditionFieldRequired", "branch1");

        var filled = await Publish(admin, "条件字段已填", BranchModel(fields: null, conditionField: "amount"));
        Assert.True(filled > 0);
    }

    private static object WebhookOnly() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "webhook",
                type = "webhook",
                name = "通知",
                props = new { webhookUrl = "https://example.com/hook" },
            },
        },
    };

    private static object WebhookInBranch() => new
    {
        version = 1,
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
                    new
                    {
                        id = "arm",
                        name = "条件",
                        isDefault = false,
                        expr = new { field = "amount", op = "gt", value = 1 },
                        next = new
                        {
                            id = "webhook",
                            type = "webhook",
                            name = "通知",
                            props = new { webhookUrl = "https://example.com/hook" },
                        },
                    },
                    new { id = "fallback", name = "默认", isDefault = true },
                },
            },
        },
    };

    private static object AiDecisionOnly() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "ai",
                type = "aiDecision",
                name = "AI review",
                props = new
                {
                    assignee = new { provider = "user", @params = new { userIds = new[] { 1L } } },
                    aiInstructions = "Review the selected case.",
                    aiInputFields = new[] { "amount" },
                    maxAttempts = 2,
                },
            },
        },
    };

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

    private static object BranchModel(string[]? fields, string? conditionField, string? nestedField = null, string? formComponent = null)
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

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
