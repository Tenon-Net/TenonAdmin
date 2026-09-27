using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 3「同一人相邻节点去重」契约测试。判据锚在 <see cref="EnterNodeOp.CreateTaskDedupedAsync"/>:
/// 与「本 token 最近一个已审批节点」的完整审批人集合求交集,交集内的人不再重复建待办;
/// 去重后无人剩余则本节点自动通过,否则只给剩余人建待办;非相邻(中间隔着已审批的别人)不去重;
/// <c>multiLeader</c>(连续多级主管)豁免——同一人跨级重现是设计意图,不是意外重复。
/// </summary>
public class WfAdjacentDedupTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Partial_duplicate_approver_is_skipped_remaining_still_gets_task()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var aId = await AddUser(admin, "wf-dedup-partial-a");
        var bId = await AddUser(admin, "wf-dedup-partial-b");
        await AddUser(admin, "wf-dedup-partial-starter");
        var definitionId = await Publish(admin, "去重-部分重复", ChainedApprovalModel(
            ("any", new[] { aId }),
            ("any", new[] { aId, bId })));

        var starter = await ClientFor(f, "wf-dedup-partial-starter");
        var a = await ClientFor(f, "wf-dedup-partial-a");
        var b = await ClientFor(f, "wf-dedup-partial-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());

        var events = await HistoryEventsFor(starter, instanceId);
        var skipped = Assert.Single(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.DuplicateApproverSkipped &&
            e.GetProperty("nodeId").GetString() == "approve-2");
        using (var payload = JsonDocument.Parse(skipped.GetProperty("payloadJson").GetString()!))
        {
            Assert.Equal(new[] { aId },
                payload.RootElement.GetProperty("userIds").EnumerateArray().Select(x => x.GetInt64()).ToArray());
        }

        // A 的部分重复被去重,approve-2 不该再给 A 建待办;B 不重复,照常建待办。
        Assert.Empty(await TodoItemsFor(a, instanceId));
        var bTodo = await TodoFor(b, instanceId);
        Assert.Equal("approve-2", bTodo.GetProperty("nodeId").GetString());

        var approve2 = await PostEnvelope(b, "/api/v1/workflow/task/approve",
            new { taskId = bTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve2.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve2.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Node_auto_passes_when_sole_approver_fully_duplicates_adjacent_node()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var aId = await AddUser(admin, "wf-dedup-full-a");
        var cId = await AddUser(admin, "wf-dedup-full-c");
        await AddUser(admin, "wf-dedup-full-starter");
        var definitionId = await Publish(admin, "去重-全部重复自动通过", ChainedApprovalModel(
            ("any", new[] { aId }),
            ("any", new[] { aId }),
            ("any", new[] { cId })));

        var starter = await ClientFor(f, "wf-dedup-full-starter");
        var a = await ClientFor(f, "wf-dedup-full-a");
        var c = await ClientFor(f, "wf-dedup-full-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            approve1.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        // approve-2 整节点被去重自动通过,直接推进到 approve-3——同一次响应里 C 就已经是新在办人。
        Assert.Equal(new[] { cId }, approve1.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToArray());

        var events = await HistoryEventsFor(starter, instanceId);
        Assert.DoesNotContain(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.TaskCreated &&
            e.GetProperty("nodeId").GetString() == "approve-2");
        Assert.Contains(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.DuplicateApproverSkipped &&
            e.GetProperty("nodeId").GetString() == "approve-2");

        var cTodo = await TodoFor(c, instanceId);
        Assert.Equal("approve-3", cTodo.GetProperty("nodeId").GetString());

        var approve3 = await PostEnvelope(c, "/api/v1/workflow/task/approve",
            new { taskId = cTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve3.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve3.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Non_adjacent_repeat_approver_is_not_deduped()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var aId = await AddUser(admin, "wf-dedup-nonadj-a");
        var bId = await AddUser(admin, "wf-dedup-nonadj-b");
        await AddUser(admin, "wf-dedup-nonadj-starter");
        var definitionId = await Publish(admin, "去重-非相邻不去重", ChainedApprovalModel(
            ("any", new[] { aId }),
            ("any", new[] { bId }),
            ("any", new[] { aId })));

        var starter = await ClientFor(f, "wf-dedup-nonadj-starter");
        var a = await ClientFor(f, "wf-dedup-nonadj-a");
        var b = await ClientFor(f, "wf-dedup-nonadj-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());

        var bTodo = await TodoFor(b, instanceId);
        var approve2 = await PostEnvelope(b, "/api/v1/workflow/task/approve",
            new { taskId = bTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve2.GetProperty("code").GetInt32());

        // A 与 approve-3 之间隔着已审批的 B(非相邻),不是「相邻重复」,必须照常建待办。
        var aTodo = await TodoFor(a, instanceId);
        Assert.Equal("approve-3", aTodo.GetProperty("nodeId").GetString());

        var events = await HistoryEventsFor(starter, instanceId);
        Assert.DoesNotContain(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.DuplicateApproverSkipped &&
            e.GetProperty("nodeId").GetString() == "approve-3");

        var approve3 = await PostEnvelope(a, "/api/v1/workflow/task/approve",
            new { taskId = aTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve3.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve3.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Sequential_dedup_preserves_order()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var aId = await AddUser(admin, "wf-dedup-seq-a");
        var bId = await AddUser(admin, "wf-dedup-seq-b");
        var cId = await AddUser(admin, "wf-dedup-seq-c");
        await AddUser(admin, "wf-dedup-seq-starter");
        var definitionId = await Publish(admin, "去重-顺序会签保序", ChainedApprovalModel(
            ("any", new[] { aId }),
            ("seq", new[] { aId, bId, cId })));

        var starter = await ClientFor(f, "wf-dedup-seq-starter");
        var a = await ClientFor(f, "wf-dedup-seq-a");
        var b = await ClientFor(f, "wf-dedup-seq-b");
        var c = await ClientFor(f, "wf-dedup-seq-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        // A 在顺序会签里被去重,剩余 [B, C] 保序建 actor;B 是唯一的 Pending 顺位,C 是 Waiting。
        Assert.Equal(new[] { bId }, approve1.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToArray());

        var bTodo = await TodoFor(b, instanceId);
        Assert.Equal("approve-2", bTodo.GetProperty("nodeId").GetString());
        Assert.Empty(await TodoItemsFor(c, instanceId));

        var approve2 = await PostEnvelope(b, "/api/v1/workflow/task/approve",
            new { taskId = bTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve2.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            approve2.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var cTodo = await TodoFor(c, instanceId);
        var approve3 = await PostEnvelope(c, "/api/v1/workflow/task/approve",
            new { taskId = cTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve3.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve3.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task MultiLeader_node_is_exempt_from_dedup()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        // level=2 链只留 level2Id 一人(中间插一级禁用主管,resolve 时被 FilterEnabledAsync 滤掉),
        // level=3 链则是 [level2Id, level3Id]——与 WorkflowMultiLeaderSnapshotTests.TwoLevelMultiLeaderModel
        // 用例同一套手法,才能让 level=2/level=3 两条链长度不同、又共享 level2Id 这个交集元素。
        var level3Id = await AddUser(admin, "wf-dedup-ml-level3");
        var level2Id = await AddUser(admin, "wf-dedup-ml-level2", directorId: level3Id);
        var disabledLevel1Id = await AddUser(admin, "wf-dedup-ml-level1", directorId: level2Id);
        await AddUser(admin, "wf-dedup-ml-starter", directorId: disabledLevel1Id);
        await SetEnabled(admin, disabledLevel1Id, false);
        var definitionId = await Publish(admin, "去重-多级主管豁免", TwoLevelMultiLeaderModel());

        var starter = await ClientFor(f, "wf-dedup-ml-starter");
        var level2 = await ClientFor(f, "wf-dedup-ml-level2");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var level2TaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var enterLevel3 = await PostEnvelope(level2, "/api/v1/workflow/task/approve", new { taskId = level2TaskId });
        Assert.Equal(0, enterLevel3.GetProperty("code").GetInt32());

        // 回归护栏(Root cause 1):level-3 链是 [level2Id, level3Id],level2Id 刚批完 level-2。
        // 若 multiLeader 没有豁免去重,level2Id 会被误当成「相邻重复」滤掉,第一顺位就会错变成 level3Id。
        Assert.Equal(new[] { level2Id }, enterLevel3.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToArray());
    }

    /// <summary>
    /// 变长审批链构造器:<c>approve-1/approve-2/...</c> 依次相连,每节点固定 provider=<c>user</c>。
    /// </summary>
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

    /// <summary>level-2(multiLeader level=2) → level-3(multiLeader level=3),用于豁免回归护栏。</summary>
    private static object TwoLevelMultiLeaderModel() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "level-2",
                type = "approval",
                name = "二级主管链",
                props = new
                {
                    assignee = new
                    {
                        provider = "multiLeader",
                        @params = new Dictionary<string, object> { ["level"] = 2 },
                    },
                    mode = "any",
                },
                next = new
                {
                    id = "level-3",
                    type = "approval",
                    name = "三级主管链",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "multiLeader",
                            @params = new Dictionary<string, object> { ["level"] = 3 },
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

    private static async Task<long> AddUser(HttpClient admin, string account, long? directorId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
            ["directorId"] = directorId,
        };
        var env = await PostEnvelope(admin, "/api/v1/sys/user", body);
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task SetEnabled(HttpClient admin, long userId, bool enabled)
    {
        var env = await (await admin.PutJson(
            $"/api/v1/sys/user/{userId}/enabled",
            new { enabled })).ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
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

    private static async Task<JsonElement> TodoFor(HttpClient client, long instanceId) =>
        Assert.Single(await TodoItemsFor(client, instanceId));

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
