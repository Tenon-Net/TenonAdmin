using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>阻断和退回发起人要在详情和「我发起的」里能看出来。</summary>
public class WfRuntimePauseTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Missing_approver_is_blocked_on_detail_and_in_my_list()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pause-block-starter");
        var definitionId = await Publish(admin, "阻断可见", LeaderModel());
        var starter = await ClientFor(f, "wf-pause-block-starter");

        var detailDef = await (await starter.GetAsync($"/api/v1/workflow/instance/startable/{definitionId}")).ReadEnvelope();
        Assert.True(detailDef.GetProperty("data").GetProperty("mayBlockWithoutApprover").GetBoolean());

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var detail = await (await starter.GetAsync($"/api/v1/workflow/instance/{instanceId}")).ReadEnvelope();
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var data = detail.GetProperty("data");
        Assert.Equal("leader", Assert.Single(data.GetProperty("blockedNodeIds").EnumerateArray()).GetString());
        Assert.Equal("leader", Assert.Single(data.GetProperty("currentNodeIds").EnumerateArray()).GetString());
        Assert.False(data.GetProperty("returnedToStarter").GetBoolean());
        Assert.False(data.GetProperty("hasPendingTask").GetBoolean());

        var mine = await (await starter.GetAsync("/api/v1/workflow/instance/page?Current=1&Size=20")).ReadEnvelope();
        var row = Assert.Single(mine.GetProperty("data").GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt64() == instanceId);
        Assert.Equal("leader", Assert.Single(row.GetProperty("blockedNodeIds").EnumerateArray()).GetString());
        Assert.False(row.GetProperty("returnedToStarter").GetBoolean());
    }

    [Fact]
    public async Task Blocked_instance_keeps_form_values_for_starter_and_admin()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pause-form-starter");
        var definitionId = await Publish(admin, "阻断仍可见表单", BlockedFormModel());
        var starter = await ClientFor(f, "wf-pause-form-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            variablesJson = """{"amount":8000,"reason":"差旅"}""",
        });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        foreach (var client in new[] { starter, admin })
        {
            var detail = await (await client.GetAsync($"/api/v1/workflow/instance/{instanceId}")).ReadEnvelope();
            Assert.Equal(0, detail.GetProperty("code").GetInt32());
            var data = detail.GetProperty("data");
            Assert.Equal("leader", Assert.Single(data.GetProperty("blockedNodeIds").EnumerateArray()).GetString());
            Assert.False(data.GetProperty("hasPendingTask").GetBoolean());
            using var values = JsonDocument.Parse(data.GetProperty("variablesJson").GetString()!);
            Assert.Equal(8000, values.RootElement.GetProperty("amount").GetDecimal());
            Assert.Equal("差旅", values.RootElement.GetProperty("reason").GetString());
            Assert.Equal(2, data.GetProperty("model").GetProperty("formSchema").GetProperty("fields").GetArrayLength());
        }
    }

    [Fact]
    public async Task Return_to_the_starter_is_marked_on_detail_and_in_my_list()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pause-return-starter");
        var approverId = await AddUser(admin, "wf-pause-return-approver");
        var definitionId = await Publish(admin, "退回发起人", ReturnToStartModel(approverId));
        var starter = await ClientFor(f, "wf-pause-return-starter");
        var approver = await ClientFor(f, "wf-pause-return-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal(0, (await PostEnvelope(approver, "/api/v1/workflow/task/return", new { taskId, comment = "办理意见" })).GetProperty("code").GetInt32());

        var detail = await (await starter.GetAsync($"/api/v1/workflow/instance/{instanceId}")).ReadEnvelope();
        var data = detail.GetProperty("data");
        Assert.True(data.GetProperty("returnedToStarter").GetBoolean());
        Assert.Equal("start", Assert.Single(data.GetProperty("currentNodeIds").EnumerateArray()).GetString());
        Assert.Empty(data.GetProperty("blockedNodeIds").EnumerateArray());
        Assert.False(data.GetProperty("hasPendingTask").GetBoolean());

        var mine = await (await starter.GetAsync("/api/v1/workflow/instance/page?Current=1&Size=20")).ReadEnvelope();
        var row = Assert.Single(mine.GetProperty("data").GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt64() == instanceId);
        Assert.True(row.GetProperty("returnedToStarter").GetBoolean());
    }

    private static object LeaderModel() => new
    {
        version = 1,
        root = new
        {
            id = "start", type = "start", name = "发起",
            next = new
            {
                id = "leader", type = "approval", name = "部门主管",
                props = new
                {
                    assignee = new { provider = "leader", @params = new { level = 1 } },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object BlockedFormModel() => new
    {
        version = 1,
        formSchema = new
        {
            version = 1,
            fields = new object[]
            {
                new { key = "amount", label = "金额", type = "money", required = true },
                new { key = "reason", label = "事由", type = "textarea", required = true },
            },
        },
        root = new
        {
            id = "start", type = "start", name = "发起",
            next = new
            {
                id = "leader", type = "approval", name = "直属主管V2",
                props = new
                {
                    assignee = new { provider = "leader", @params = new { level = 1 } },
                    mode = "any",
                    formPerms = new object[]
                    {
                        new { field = "amount", access = "hidden" },
                        new { field = "reason", access = "hidden" },
                    },
                },
                next = (object?)null,
            },
        },
    };

    private static object ReturnToStartModel(long approverId) => new
    {
        version = 1,
        root = new
        {
            id = "start", type = "start", name = "发起",
            next = new
            {
                id = "approval", type = "approval", name = "审批",
                props = new
                {
                    assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { approverId } } },
                    mode = "any",
                    returnPolicy = "prev",
                },
                next = (object?)null,
            },
        },
    };

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account, password = Password, name = account, enabled = true, orgId = 1, roleIds = new[] { 2L },
        });
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        Assert.Equal(0, (await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id })).GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
