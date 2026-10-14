using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>待办和我发起的列表给出发起人姓名和表单摘要,不回退成用户 Id。</summary>
public class WfListSummaryTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Todo_and_mine_lists_show_applicant_name_and_form_summary()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var approverId = await AddUser(admin, "wf-sum-approver", "李审批");
        await AddUser(admin, "wf-sum-starter", "张申请");
        var definitionId = await Publish(admin, "出差报销", Model(approverId));
        var starter = await ClientFor(f, "wf-sum-starter");
        var approver = await ClientFor(f, "wf-sum-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            variablesJson = """{"reason":"去上海见客户","days":3}""",
        });
        Assert.Equal(0, start.GetProperty("code").GetInt32());

        var todo = await (await approver.GetAsync("/api/v1/workflow/task/todo?Current=1&Size=20")).ReadEnvelope();
        var todoRow = Assert.Single(todo.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("张申请", todoRow.GetProperty("starterName").GetString());
        Assert.Equal("审批", todoRow.GetProperty("nodeName").GetString());
        Assert.Contains("事由：去上海见客户", todoRow.GetProperty("summary").GetString());
        Assert.Contains("天数：3", todoRow.GetProperty("summary").GetString());

        var mine = await (await starter.GetAsync("/api/v1/workflow/instance/page?Current=1&Size=20")).ReadEnvelope();
        var mineRow = Assert.Single(mine.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("张申请", mineRow.GetProperty("starterName").GetString());
        Assert.Contains("事由：去上海见客户", mineRow.GetProperty("summary").GetString());
    }

    private static object Model(long approverId) => new
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
                },
                next = (object?)null,
            },
        },
        formSchema = new
        {
            version = 1,
            fields = new object[]
            {
                new { key = "reason", label = "事由", type = "text", required = true },
                new { key = "days", label = "天数", type = "number", required = true },
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

    private static async Task<long> AddUser(HttpClient admin, string account, string name)
    {
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account, password = Password, name, enabled = true, orgId = 1, roleIds = new[] { 2L },
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
