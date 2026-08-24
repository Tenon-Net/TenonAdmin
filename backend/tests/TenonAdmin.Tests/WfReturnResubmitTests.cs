using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 5「主动退回 + 退回重提」契约测试。
/// Return:当前办理人按节点 <see cref="WfReturnPolicy"/> 把待办退回给之前某节点——关闭当前活跃任务
/// (不像转办那样继续等人),token 回退,不自动继续,等发起人重提。
/// Resubmit:仅发起人、仅退回后无活跃待办的 Running 实例可重提;从 <c>start</c> 重新走一遍。
/// </summary>
public class WfReturnResubmitTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Return_with_node_policy_closes_current_task_without_auto_continuing()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-node-starter");
        var aId = await AddUser(admin, "wf-return-node-a");
        var bId = await AddUser(admin, "wf-return-node-b");
        var definitionId = await Publish(admin, "退回-Node策略", ReturnNodePolicyModel(aId, bId));

        var starter = await ClientFor(f, "wf-return-node-starter");
        var a = await ClientFor(f, "wf-return-node-a");
        var b = await ClientFor(f, "wf-return-node-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            ret.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        Assert.Empty(await TodoItemsFor(b, instanceId));
        Assert.Empty(await TodoItemsFor(a, instanceId));

        // 关闭活跃任务是否真的物理清了 wf_task_actor / wf_task(不止是靠 CAS 认领把办理人自己那行翻 Skipped)。
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            Assert.Equal(0, await db.Queryable<WfTaskActor>().Where(x => x.TaskId == task2Id).CountAsync());
            Assert.Equal(0, await db.Queryable<WfTask>().Where(x => x.Id == task2Id).CountAsync());
        }

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var hisTasks = detail.GetProperty("data").GetProperty("hisTasks").EnumerateArray().ToList();
        Assert.Contains(hisTasks, h =>
            h.GetProperty("userId").GetInt64() == bId && h.GetProperty("action").GetInt32() == (int)WfTaskAction.Return);
    }

    [Fact]
    public async Task Return_with_prev_policy_falls_back_to_start_when_no_prior_approval()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-prev-starter");
        var aId = await AddUser(admin, "wf-return-prev-a");
        var definitionId = await Publish(admin, "退回-Prev策略无先例", ReturnPrevPolicyModel(aId));

        var starter = await ClientFor(f, "wf-return-prev-starter");
        var a = await ClientFor(f, "wf-return-prev-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(a, "/api/v1/workflow/task/return", new { taskId });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Return_with_any_policy_rejects_unwalked_target()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-any-starter");
        var aId = await AddUser(admin, "wf-return-any-a");
        var bId = await AddUser(admin, "wf-return-any-b");
        var cId = await AddUser(admin, "wf-return-any-c");
        var definitionId = await Publish(admin, "退回-Any策略非法目标", ReturnAnyPolicyModel(aId, bId, cId));

        var starter = await ClientFor(f, "wf-return-any-starter");
        var a = await ClientFor(f, "wf-return-any-a");
        var b = await ClientFor(f, "wf-return-any-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // node3 是模型里真实存在的节点(FindNode 能解析),但本实例从未走到过——必须被"已走节点"校验拦下。
        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return",
            new { taskId = task2Id, targetNodeId = "node3" });
        Assert.Equal(WorkflowErrorCode.ReturnNotAllowed, ret.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// 三节点链(start→node1[A]→node2[B]→node3[C,returnPolicy=node,returnToNodeId=node1])。用三个不同审批人
    /// 而非「node2 退回直接回 node1」的两节点链——理由与 <see cref="WfRejectRoutingTests"/> 里的
    /// <c>RejectRouteModel</c> 完全一致:重提从 <c>start</c> 重新走一遍时复用同一个 token,若退回目标恰好是
    /// 「最近一次 Approve 记录」所在节点,重新进入 node1 会被 Task 3「同一人相邻节点去重」误判为已审过而自动
    /// 跳过(去重只看最近一条 Approve 历史行的审批人集合,不区分是正向推进、拒绝回退还是重提重走)。让 A、B
    /// 都先通过,使「最近一次 Approve」落在 node2/B 而非 node1/A,规避这一正交的去重路径。
    /// </summary>
    [Fact]
    public async Task Starter_can_resubmit_after_return_and_flow_walks_from_start_again()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-resubmit-core-starter");
        var aId = await AddUser(admin, "wf-resubmit-core-a");
        var bId = await AddUser(admin, "wf-resubmit-core-b");
        var cId = await AddUser(admin, "wf-resubmit-core-c");
        var definitionId = await Publish(admin, "重提-核心用例", ResubmitCoreModel(aId, bId, cId));

        var starter = await ClientFor(f, "wf-resubmit-core-starter");
        var a = await ClientFor(f, "wf-resubmit-core-a");
        var b = await ClientFor(f, "wf-resubmit-core-b");
        var c = await ClientFor(f, "wf-resubmit-core-c");

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

        var ret = await PostEnvelope(c, "/api/v1/workflow/task/return", new { taskId = task3Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());

        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new { instanceId });
        Assert.Equal(0, resubmit.GetProperty("code").GetInt32());

        var aTodo = await TodoItemsFor(a, instanceId);
        Assert.Contains(aTodo, item => item.GetProperty("nodeId").GetString() == "node1");

        var history = await GetEnvelope(starter, $"/api/v1/workflow/instance/history/{instanceId}");
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        Assert.Contains(history.GetProperty("data").EnumerateArray(),
            e => e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.InstanceResubmitted);
    }

    /// <summary>
    /// 先让 B 退回,让实例进入「Running 但无活跃待办」的可重提状态,再由非发起人 A(链上的参与者,不是
    /// 发起人)调用重提——这样唯一能拦下它的就是「发起人校验」本身。若这里改用「有活跃待办」的实例,
    /// 「无活跃待办」校验会先一步拦下,「发起人校验」被删掉时本条测试也不会变红,起不到变异验证的作用。
    /// </summary>
    [Fact]
    public async Task Non_starter_cannot_resubmit()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-resubmit-notstarter-starter");
        var aId = await AddUser(admin, "wf-resubmit-notstarter-a");
        var bId = await AddUser(admin, "wf-resubmit-notstarter-b");
        var definitionId = await Publish(admin, "重提-非发起人", ReturnNodePolicyModel(aId, bId));

        var starter = await ClientFor(f, "wf-resubmit-notstarter-starter");
        var a = await ClientFor(f, "wf-resubmit-notstarter-a");
        var b = await ClientFor(f, "wf-resubmit-notstarter-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());

        var resubmit = await PostEnvelope(a, "/api/v1/workflow/instance/resubmit", new { instanceId });
        Assert.Equal(WorkflowErrorCode.ResubmitNotAllowed, resubmit.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Cannot_resubmit_when_instance_has_active_task()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-resubmit-active-starter");
        var aId = await AddUser(admin, "wf-resubmit-active-a");
        var bId = await AddUser(admin, "wf-resubmit-active-b");
        var definitionId = await Publish(admin, "重提-仍有活跃待办", ReturnNodePolicyModel(aId, bId));

        var starter = await ClientFor(f, "wf-resubmit-active-starter");
        var a = await ClientFor(f, "wf-resubmit-active-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // A 批完 node1,B 在 node2 有活跃待办,没人退回。
        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());

        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new { instanceId });
        Assert.Equal(WorkflowErrorCode.ResubmitNotAllowed, resubmit.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Resubmit_with_new_variables_json_overrides_instance_data()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-resubmit-vars-starter");
        var aId = await AddUser(admin, "wf-resubmit-vars-a");
        var bId = await AddUser(admin, "wf-resubmit-vars-b");
        var definitionId = await Publish(admin, "重提-覆盖变量", ReturnNodePolicyModel(aId, bId));

        var starter = await ClientFor(f, "wf-resubmit-vars-starter");
        var a = await ClientFor(f, "wf-resubmit-vars-a");
        var b = await ClientFor(f, "wf-resubmit-vars-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());

        const string newVariablesJson = "{\"amount\":999}";
        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit",
            new { instanceId, variablesJson = newVariablesJson });
        Assert.Equal(0, resubmit.GetProperty("code").GetInt32());

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        Assert.Equal(newVariablesJson, detail.GetProperty("data").GetProperty("variablesJson").GetString());
    }

    /// <summary>start → node1(any,[A]) → node2(any,[B]) → node3(any,[C],returnPolicy=node,returnToNodeId=node1) → null。</summary>
    private static object ResubmitCoreModel(long aUserId, long bUserId, long cUserId) => new
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
                            returnPolicy = "node",
                            returnToNodeId = "node1",
                        },
                        next = (object?)null,
                    },
                },
            },
        },
    };

    /// <summary>start → node1(any,[A]) → node2(any,[B],returnPolicy=node,returnToNodeId=node1) → null。</summary>
    private static object ReturnNodePolicyModel(long aUserId, long bUserId) => new
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
                        returnPolicy = "node",
                        returnToNodeId = "node1",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    /// <summary>start → node1(any,[A],returnPolicy=prev) → null;node1 是链上第一个审批节点。</summary>
    private static object ReturnPrevPolicyModel(long aUserId) => new
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
                    returnPolicy = "prev",
                },
                next = (object?)null,
            },
        },
    };

    /// <summary>start → node1(any,[A]) → node2(any,[B],returnPolicy=any) → node3(any,[C]) → null。</summary>
    private static object ReturnAnyPolicyModel(long aUserId, long bUserId, long cUserId) => new
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
                        returnPolicy = "any",
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
