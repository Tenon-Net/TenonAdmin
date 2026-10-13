using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 受限办理人打开详情时仍要看到流程名和发起人姓名。
/// 定义多由超管创建且没有机构,机构过滤器会把名称查空。
/// </summary>
public class WfInstanceDetailVisibilityTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Restricted_approver_sees_definition_name_and_starter_name()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-name-starter", "张申请", orgId: 1, roleIds: [2]);
        var approverId = await AddUser(admin, "wf-name-approver", "李审批", orgId: 7, roleIds: [3]);
        var definitionId = await Publish(admin, "跨机构报销", SingleModel(approverId));
        var starter = await ClientFor(f, "wf-name-starter");
        var approver = await ClientFor(f, "wf-name-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var detail = await (await approver.GetAsync($"/api/v1/workflow/instance/{instanceId}")).ReadEnvelope();
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var data = detail.GetProperty("data");
        Assert.Equal("跨机构报销", data.GetProperty("definitionName").GetString());
        Assert.Equal("张申请", data.GetProperty("starterName").GetString());
    }

    private static object SingleModel(long approverId) => new
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
    };

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account, string name, long orgId, long[] roleIds)
    {
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account, password = Password, name, enabled = true, orgId, roleIds,
        });
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, groupName = "财务", icon = "ph:receipt", model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        Assert.Equal(0, (await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id })).GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
