using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 13:流程图回放按最后一次节点访问收敛 + 监控列表参与业务过滤。
/// <para>变异:丢掉 <c>CollectVisitedNodeIds</c> 的跳转 cutoff → 回退前旧节点出现在 visited;
/// 监控三筛去掉任一 userId 条件 → 对方实例漏过来;
/// <c>EnsureParticipantAsync</c> 对路人放行 → 48015 变 0。</para>
/// </summary>
public class WfReplayMonitorTests
{
    private const string Password = "Test@123456";

    /// <summary>
    /// start→node1[A]→node2[B] 退回后重提:旧路径的 node2 不得再点亮。
    /// </summary>
    [Fact]
    public async Task Last_visit_after_return_and_resubmit_excludes_discarded_approval_node()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-replay-resubmit-starter");
        var aId = await AddUser(admin, "wf-replay-resubmit-a");
        var bId = await AddUser(admin, "wf-replay-resubmit-b");
        var definitionId = await Publish(admin, "回放-退回重提", TwoNodeReturnModel(aId, bId));

        var starter = await ClientFor(f, "wf-replay-resubmit-starter");
        var a = await ClientFor(f, "wf-replay-resubmit-a");
        var b = await ClientFor(f, "wf-replay-resubmit-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());

        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new { instanceId });
        Assert.Equal(0, resubmit.GetProperty("code").GetInt32());

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var data = detail.GetProperty("data");
        Assert.Equal(JsonValueKind.Object, data.GetProperty("model").ValueKind);
        Assert.Equal("start", data.GetProperty("model").GetProperty("root").GetProperty("id").GetString());

        var visited = NodeIds(data.GetProperty("visitedNodeIds"));
        Assert.Contains("start", visited);
        Assert.Contains("node1", visited);
        Assert.DoesNotContain("node2", visited);
    }

    /// <summary>
    /// start→node1[A]→node2[B,onReject=toNode→node1]:拒绝后只点亮跳转后窗口,node2 是丢弃前缀。
    /// </summary>
    [Fact]
    public async Task Last_visit_after_reject_to_node_excludes_discarded_path()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-replay-reject-starter");
        var aId = await AddUser(admin, "wf-replay-reject-a");
        var bId = await AddUser(admin, "wf-replay-reject-b");
        var definitionId = await Publish(admin, "回放-拒绝路由", TwoNodeRejectModel(aId, bId));

        var starter = await ClientFor(f, "wf-replay-reject-starter");
        var a = await ClientFor(f, "wf-replay-reject-a");
        var b = await ClientFor(f, "wf-replay-reject-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(b, "/api/v1/workflow/task/reject", new { taskId = task2Id });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var data = detail.GetProperty("data");
        var visited = NodeIds(data.GetProperty("visitedNodeIds"));
        Assert.Contains("node1", visited);
        Assert.DoesNotContain("node2", visited);
        var current = NodeIds(data.GetProperty("currentNodeIds"));
        Assert.Contains("node1", current);
    }

    /// <summary>
    /// 监控三筛各自只看见对应实例;超管能打开自己没参与的详情;路人仍 48015。
    /// </summary>
    [Fact]
    public async Task Monitor_page_filters_starter_actor_and_cc_independently()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterSId = await AddUser(admin, "wf-mon-starter-s");
        var hangSId = await AddUser(admin, "wf-mon-hang-s");
        var starterAId = await AddUser(admin, "wf-mon-starter-a");
        var actorAId = await AddUser(admin, "wf-mon-actor-a");
        var starterCId = await AddUser(admin, "wf-mon-starter-c");
        var ccCId = await AddUser(admin, "wf-mon-cc-c");
        var hangCId = await AddUser(admin, "wf-mon-hang-c");
        var strangerId = await AddUser(admin, "wf-mon-stranger");
        _ = strangerId;

        var defS = await Publish(admin, "监控-发起人单", AnyApprovalModel(hangSId));
        var defA = await Publish(admin, "监控-办理人单", AnyApprovalModel(actorAId));
        var defC = await Publish(admin, "监控-抄送人单", CcThenApprovalModel(ccCId, hangCId));

        var starterS = await ClientFor(f, "wf-mon-starter-s");
        var starterA = await ClientFor(f, "wf-mon-starter-a");
        var actorA = await ClientFor(f, "wf-mon-actor-a");
        var starterC = await ClientFor(f, "wf-mon-starter-c");
        var stranger = await ClientFor(f, "wf-mon-stranger");

        var startedS = await PostEnvelope(starterS, "/api/v1/workflow/instance/start", new { definitionId = defS });
        Assert.Equal(0, startedS.GetProperty("code").GetInt32());
        var sId = startedS.GetProperty("data").GetProperty("instanceId").GetInt64();

        var startedA = await PostEnvelope(starterA, "/api/v1/workflow/instance/start", new { definitionId = defA });
        Assert.Equal(0, startedA.GetProperty("code").GetInt32());
        var aInstanceId = startedA.GetProperty("data").GetProperty("instanceId").GetInt64();
        var aTaskId = startedA.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var approved = await PostEnvelope(actorA, "/api/v1/workflow/task/approve", new { taskId = aTaskId });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());

        var startedC = await PostEnvelope(starterC, "/api/v1/workflow/instance/start", new { definitionId = defC });
        Assert.Equal(0, startedC.GetProperty("code").GetInt32());
        var cId = startedC.GetProperty("data").GetProperty("instanceId").GetInt64();

        var byStarter = await GetEnvelope(admin,
            $"/api/v1/workflow/instance/monitor?Current=1&Size=20&StarterUserId={starterSId}");
        Assert.Equal(0, byStarter.GetProperty("code").GetInt32());
        Assert.Equal([sId], PageIds(byStarter));

        var byActor = await GetEnvelope(admin,
            $"/api/v1/workflow/instance/monitor?Current=1&Size=20&ActorUserId={actorAId}");
        Assert.Equal(0, byActor.GetProperty("code").GetInt32());
        Assert.Equal([aInstanceId], PageIds(byActor));

        var byCc = await GetEnvelope(admin,
            $"/api/v1/workflow/instance/monitor?Current=1&Size=20&CcUserId={ccCId}");
        Assert.Equal(0, byCc.GetProperty("code").GetInt32());
        Assert.Equal([cId], PageIds(byCc));

        var adminDetail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{sId}");
        Assert.Equal(0, adminDetail.GetProperty("code").GetInt32());
        Assert.Equal(sId, adminDetail.GetProperty("data").GetProperty("id").GetInt64());

        var denied = await GetEnvelope(stranger, $"/api/v1/workflow/instance/{sId}");
        Assert.Equal(WorkflowErrorCode.InstanceAccessDenied, denied.GetProperty("code").GetInt32());
    }

    /// <summary>未授监控权限的登录用户调 monitor 被 RolePermission 挡下(超管才能裸调)。</summary>
    [Fact]
    public async Task Monitor_page_rejects_user_without_permission()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-mon-noperm");
        var user = await ClientFor(f, "wf-mon-noperm");
        var resp = await user.GetAsync("/api/v1/workflow/instance/monitor?Current=1&Size=20");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private static List<string> NodeIds(JsonElement arr) =>
        arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList();

    private static List<long> PageIds(JsonElement envelope) =>
        envelope.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt64())
            .ToList();

    private static object TwoNodeReturnModel(long aUserId, long bUserId) => new
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
                        returnPolicy = "prev",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    private static object TwoNodeRejectModel(long aUserId, long bUserId) => new
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

    private static object AnyApprovalModel(long userId) => new
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
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object CcThenApprovalModel(long ccUserId, long approverId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "cc1",
                type = "cc",
                name = "抄送",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { ccUserId } },
                    },
                },
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
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { approverId } },
                        },
                        mode = "any",
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
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
