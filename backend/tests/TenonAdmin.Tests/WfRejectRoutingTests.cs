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
    /// 三节点链(start→node1[A]→node2[B]→node3[C,onReject=toNode→node1]):验证<b>拒绝路由本身</b>——
    /// 拒绝不终止实例、目标节点重新建待办、跳过的中间节点不受影响。跨越两个节点回跳,与「同一人相邻节点
    /// 去重」的基线判定不重叠,所以这条用例只钉住拒绝路由这一件事。
    /// 「回跳目标恰好就是紧邻的上一个已审批节点」这条更常见的配置由
    /// <see cref="Reject_to_immediately_previous_node_reassigns_that_nodes_approver"/> 用两节点链专门钉住
    /// (那条锚的是「向后跳转重置去重基线」)。
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

    /// <summary>
    /// 两节点链(start→node1[A]→node2[B,onReject=toNode→node1])——「退回上一步」这个最常见的真实配置。
    /// 回跳目标 node1 正好是「最近一条 Approve 行」所在的节点,若去重基线不因向后跳转重置,node1 会被判成
    /// 「A 已审过」而整节点自动通过,token 立刻落回 node2 → 拒绝人 B 把待办原地弹回给自己(可无限循环),
    /// 拒绝路由在其唯一常用配置下退化成空操作。故这里钉的是<b>A 的待办重新出现在 node1</b>,而不是
    /// B 拿回自己的待办。
    /// </summary>
    [Fact]
    public async Task Reject_to_immediately_previous_node_reassigns_that_nodes_approver()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-reject-prev-starter");
        var aId = await AddUser(admin, "wf-reject-prev-a");
        var bId = await AddUser(admin, "wf-reject-prev-b");
        var definitionId = await Publish(admin, "拒绝-回跳紧邻上一节点", TwoNodeRejectRouteModel(aId, bId));

        var starter = await ClientFor(f, "wf-reject-prev-starter");
        var a = await ClientFor(f, "wf-reject-prev-a");
        var b = await ClientFor(f, "wf-reject-prev-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(b, "/api/v1/workflow/task/reject", new { taskId = task2Id });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            reject.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.Equal(new[] { aId }, reject.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToArray());

        var aTodo = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal("node1", aTodo.GetProperty("nodeId").GetString());
        Assert.Empty(await TodoItemsFor(b, instanceId));
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

    /// <summary>start → node1(any,[A]) → node2(any,[B],onReject=toNode,rejectToNodeId=node1) → null。</summary>
    private static object TwoNodeRejectRouteModel(long aUserId, long bUserId) => new
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
                        onReject = "toNode",
                        rejectToNodeId = "node1",
                    },
                    next = (object?)null,
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
