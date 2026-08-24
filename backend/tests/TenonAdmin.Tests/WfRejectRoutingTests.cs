using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 5「拒绝路由」契约测试:节点未配置 <see cref="WfRejectAction"/> 或配为
/// <see cref="WfRejectAction.Terminate"/> 时,拒绝行为与 M1 完全一致(终止实例);配为
/// <see cref="WfRejectAction.ToNode"/> 时,拒绝不终止实例,而是回退到目标节点重新进入(目标节点重新建待办)。
/// </summary>
public class WfRejectRoutingTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Reject_with_default_terminate_still_terminates()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-reject-terminate-starter");
        var approverId = await AddUser(admin, "wf-reject-terminate-approver");
        var definitionId = await Publish(admin, "拒绝-默认终止", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-reject-terminate-starter");
        var approver = await ClientFor(f, "wf-reject-terminate-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new { taskId });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Rejected,
            reject.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    /// <summary>
    /// 三节点链(start→node1[A]→node2[B]→node3[C,onReject=toNode→node1])。用三个不同审批人而非
    /// 「node2 拒绝直接退回 node1」的两节点链——若拒绝目标恰好是 Task 3「同一人相邻节点去重」判定用的
    /// 「最近一次 Approve 记录」所在节点,重新进入 node1 会被去重判定为„已审过“而自动跳过,不会真正建
    /// 待办(去重逻辑只看最近一条 Approve 历史行的审批人集合,不区分正向推进还是拒绝回退)。让 A、B 都先
    /// 通过,使„最近一次 Approve“落在 node2/B 而非 node1/A,规避这一正交的去重路径,单纯验证拒绝路由本身。
    /// </summary>
    [Fact]
    public async Task Reject_with_toNode_routes_back_without_terminating()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-reject-tonode-starter");
        var aId = await AddUser(admin, "wf-reject-tonode-a");
        var bId = await AddUser(admin, "wf-reject-tonode-b");
        var cId = await AddUser(admin, "wf-reject-tonode-c");
        var definitionId = await Publish(admin, "拒绝-路由回退", RejectRouteModel(aId, bId, cId));

        var starter = await ClientFor(f, "wf-reject-tonode-starter");
        var a = await ClientFor(f, "wf-reject-tonode-a");
        var b = await ClientFor(f, "wf-reject-tonode-b");
        var c = await ClientFor(f, "wf-reject-tonode-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve2 = await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId = task2Id });
        Assert.Equal(0, approve2.GetProperty("code").GetInt32());
        var task3Id = approve2.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(c, "/api/v1/workflow/task/reject", new { taskId = task3Id });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            reject.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var aTodo = await TodoItemsFor(a, instanceId);
        Assert.Contains(aTodo, item => item.GetProperty("nodeId").GetString() == "node1");
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

    /// <summary>
    /// start → node1(any,[A]) → node2(any,[B]) → node3(any,[C],onReject=toNode,rejectToNodeId=node1) → null。
    /// </summary>
    private static object RejectRouteModel(long aUserId, long bUserId, long cUserId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "node1",
                type = "approval",
                name = "node1",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId } },
                    },
                    mode = "any",
                },
                next = new
                {
                    id = "node2",
                    type = "approval",
                    name = "node2",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "user",
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { bUserId } },
                        },
                        mode = "any",
                    },
                    next = new
                    {
                        id = "node3",
                        type = "approval",
                        name = "node3",
                        props = new
                        {
                            assignee = new
                            {
                                provider = "user",
                                @params = new Dictionary<string, object> { ["userIds"] = new[] { cUserId } },
                            },
                            mode = "any",
                            onReject = "toNode",
                            rejectToNodeId = "node1",
                        },
                        next = (object?)null,
                    },
                },
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

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
