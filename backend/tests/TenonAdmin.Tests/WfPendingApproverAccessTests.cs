using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 待办办理人停用后再启用,只要 Pending 审批人行还在,就能打开详情。
/// 停用只作废会话,不改办理人;48015 只拒绝既不是参与人也不是监控人的访问。
/// </summary>
public class WfPendingApproverAccessTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Reenabled_pending_approver_can_open_instance_detail()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-access-starter", "张申请", orgId: 1, roleIds: [2]);
        var approverId = await AddUser(admin, "wf-access-lisi", "李四", orgId: 7, roleIds: [3]);
        var definitionId = await Publish(admin, "停用再启用", SingleModel(approverId));
        var starter = await ClientFor(f, "wf-access-starter");
        var approver = await ClientFor(f, "wf-access-lisi");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var before = await (await approver.GetAsync($"/api/v1/workflow/instance/{instanceId}")).ReadEnvelope();
        Assert.Equal(0, before.GetProperty("code").GetInt32());
        Assert.Equal("审批", before.GetProperty("data").GetProperty("myPendingTasks")[0].GetProperty("nodeName").GetString());

        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/sys/user/{approverId}/enabled", new { enabled = false })).ReadEnvelope()).GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<IWfInstanceService>()
                .GetAsync(instanceId, approverId);
            Assert.Equal("停用再启用", detail.DefinitionName);
            Assert.Equal("审批", Assert.Single(detail.MyPendingTasks).NodeName);
        }

        var revoked = await approver.GetAsync($"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, revoked.StatusCode);

        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/sys/user/{approverId}/enabled", new { enabled = true })).ReadEnvelope()).GetProperty("code").GetInt32());
        var again = await ClientFor(f, "wf-access-lisi");
        var after = await (await again.GetAsync($"/api/v1/workflow/instance/{instanceId}")).ReadEnvelope();
        Assert.Equal(0, after.GetProperty("code").GetInt32());
        Assert.Equal("停用再启用", after.GetProperty("data").GetProperty("definitionName").GetString());
        Assert.Equal("审批", after.GetProperty("data").GetProperty("myPendingTasks")[0].GetProperty("nodeName").GetString());
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
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        Assert.Equal(0, (await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id })).GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
