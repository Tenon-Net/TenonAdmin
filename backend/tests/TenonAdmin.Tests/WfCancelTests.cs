using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 4「撤销」契约测试:发起人可撤销自己发起、尚无人已批的 Running 实例——改实例/token 状态、
/// 清活跃任务的 actor + task 行、追加 <see cref="WfHistoryEventType.InstanceCompleted"/> 历史事件
/// (payload.status = <c>"Cancelled"</c>)。非发起人撤销、或已有人 Approve 后撤销、或对非 Running 实例撤销,
/// 均按 <see cref="WorkflowErrorCode.CancelNotAllowed"/> / <see cref="WorkflowErrorCode.InstanceStatusConflict"/> 拒绝。
/// </summary>
public class WfCancelTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Starter_can_cancel_before_anyone_approves()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cancel-ok-starter");
        var approverId = await AddUser(admin, "wf-cancel-ok-approver");
        var definitionId = await Publish(admin, "撤销-发起人撤销", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cancel-ok-starter");
        var approver = await ClientFor(f, "wf-cancel-ok-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(0, cancel.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Cancelled,
            cancel.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var events = await HistoryEventsFor(starter, instanceId);
        var completed = Assert.Single(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.InstanceCompleted);
        using (var payload = JsonDocument.Parse(completed.GetProperty("payloadJson").GetString()!))
        {
            Assert.Equal("Cancelled", payload.RootElement.GetProperty("status").GetString());
        }

        Assert.Empty(await TodoItemsFor(approver, instanceId));
    }

    [Fact]
    public async Task Non_starter_cannot_cancel()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cancel-notstarter-starter");
        var approverId = await AddUser(admin, "wf-cancel-notstarter-approver");
        var definitionId = await Publish(admin, "撤销-非发起人", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cancel-notstarter-starter");
        var approver = await ClientFor(f, "wf-cancel-notstarter-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var cancel = await PostEnvelope(approver, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(WorkflowErrorCode.CancelNotAllowed, cancel.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Cannot_cancel_after_someone_approved()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cancel-approved-starter");
        var aId = await AddUser(admin, "wf-cancel-approved-a");
        var bId = await AddUser(admin, "wf-cancel-approved-b");
        var definitionId = await Publish(admin, "撤销-已有人批准", ChainedApprovalModel(
            ("any", new[] { aId }),
            ("any", new[] { bId })));

        var starter = await ClientFor(f, "wf-cancel-approved-starter");
        var a = await ClientFor(f, "wf-cancel-approved-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());

        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(WorkflowErrorCode.CancelNotAllowed, cancel.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Cannot_cancel_non_running_instance()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cancel-nonrunning-starter");
        var approverId = await AddUser(admin, "wf-cancel-nonrunning-approver");
        var definitionId = await Publish(admin, "撤销-非运行态", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cancel-nonrunning-starter");
        var approver = await ClientFor(f, "wf-cancel-nonrunning-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(WorkflowErrorCode.InstanceStatusConflict, cancel.GetProperty("code").GetInt32());
    }

    private static object AnyApprovalModel(params long[] userIds) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = userIds },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object ChainedApprovalModel(params (string Mode, long[] UserIds)[] nodes)
    {
        object? BuildFrom(int index)
        {
            if (index >= nodes.Length)
                return null;

            var (mode, userIds) = nodes[index];
            return new
            {
                id = $"approve-{index + 1}",
                type = "approval",
                name = $"approve-{index + 1}",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = userIds },
                    },
                    mode,
                },
                next = BuildFrom(index + 1),
            };
        }

        return new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = BuildFrom(0),
            },
        };
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
        };
        var env = await PostEnvelope(admin, "/api/v1/sys/user", body);
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var id = await AddDefinition(admin, name, model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<long> AddDefinition(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        return added.GetProperty("data").GetInt64();
    }

    private static async Task<List<JsonElement>> TodoItemsFor(HttpClient client, long instanceId)
    {
        var todo = await GetEnvelope(client, "/api/v1/workflow/task/todo?Current=1&Size=20");
        return todo.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("instanceId").GetInt64() == instanceId)
            .ToList();
    }

    private static async Task<List<JsonElement>> HistoryEventsFor(HttpClient client, long instanceId)
    {
        var history = await GetEnvelope(client, $"/api/v1/workflow/instance/history/{instanceId}");
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        return history.GetProperty("data").EnumerateArray().ToList();
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
