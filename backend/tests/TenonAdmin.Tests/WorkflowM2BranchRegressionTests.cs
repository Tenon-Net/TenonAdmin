using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2a Task 2「branch 节点执行 + 臂子链汇合」引擎 E2E 回归线。经 <see cref="WorkflowAppFactory"/> 走 HTTP,
/// 判据是能不能杀死两个已验证过「绿得没有意义」的变异:
/// ① <see cref="TakeTransitionOp"/> 把 <c>ctx.ResolveMergeTarget(FromNode)</c> 退回 <c>FromNode.Next</c>
///    (等于 M1 行为——走完第一条臂整单直接「通过」完结,不去汇合节点);
/// ② <see cref="WfExecutionContext.FindNode"/> 把 <see cref="WfModelIndex.Find"/> 退回 M1 主链线性扫描
///    (等于臂内待办一审批就抛 <see cref="WorkflowErrorCode.ModelInvalid"/>(48002),分支功能整个是死的)。
/// 模型固定为 start → branch(armHigh: amount&gt;100 → high-approve;armLow: 默认,Next=null) → merge-approve,
/// 审批人 provider 用 <c>initiator</c>(发起人自批),省去多用户搭建,专注分支/汇合本身。
/// </summary>
public class WorkflowM2BranchRegressionTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Arm_with_condition_creates_todo_inside_arm_then_merges_to_branch_next()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-branch-hit");
        var mergeApproverId = await AddUser(admin, "wf-branch-merge");
        var definitionId = await Publish(admin, "命中臂汇合", BranchModel(mergeApproverId));

        var starter = await ClientFor(f, "wf-branch-hit");
        var merger = await ClientFor(f, "wf-branch-merge");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId, variablesJson = """{"amount":200}""" });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        // 命中 armHigh(amount=200 > 100):待办应落在臂内节点 high-approve —— 杀变异②
        // (M1 主链线性扫描找不到臂内节点,ResolveNode 会返回 null,approve 会抛 48002)。
        var firstTodo = await TodoFor(starter, instanceId);
        Assert.Equal("high-approve", firstTodo.GetProperty("nodeId").GetString());
        // 臂内待办的 nodeName 必须解析出配置的中文节点名,不能因为「主链线性扫描找不到臂内节点」而恒为 null
        // (前端 r.nodeName || r.nodeId 兜底会静默显示设计器内部 Id)—— Round 12 评审 P1。
        Assert.Equal("high-approve", firstTodo.GetProperty("nodeName").GetString());

        // 实例详情接口(WfInstanceService.ResolveNodeName 的唯一调用点)同样要能解析出臂内节点名,
        // 不能因为退回主链线性扫描而在这条独立接线上悄悄返回 null —— Round 14 评审 P1 另一半。
        var detailBeforeApprove1 = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal("high-approve",
            detailBeforeApprove1.GetProperty("data").GetProperty("myPendingTask").GetProperty("nodeName").GetString());

        var approve1 = await PostEnvelope(starter, "/api/v1/workflow/task/approve",
            new { taskId = firstTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32()); // 不得抛 48002(ModelInvalid)—— 杀变异②
        Assert.Equal((int)WfInstanceStatus.Running,
            approve1.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        // 臂子链末节点(high-approve.Next==null)离开后应汇合到 branch1.Next(merge-approve),
        // 不应该直接完结实例 —— 杀变异①。
        // merge-approve 的审批人是独立于 starter 的专职用户(而非 initiator 自批),避免与
        // Task 3「同一人相邻节点去重」的默认行为产生巧合冲突(starter 若连批两个相邻节点会被去重)。
        var secondTodo = await TodoFor(merger, instanceId);
        Assert.Equal("merge-approve", secondTodo.GetProperty("nodeId").GetString());
        Assert.Equal("merge-approve", secondTodo.GetProperty("nodeName").GetString());

        // 对照组:汇合后主链节点在同一接口下仍应解析正常,证明改动没有连带破坏主链侧。
        var detailBeforeApprove2 = await GetEnvelope(merger, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal("merge-approve",
            detailBeforeApprove2.GetProperty("data").GetProperty("myPendingTask").GetProperty("nodeName").GetString());

        var approve2 = await PostEnvelope(merger, "/api/v1/workflow/task/approve",
            new { taskId = secondTodo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve2.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve2.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Default_arm_without_subchain_merges_directly_to_branch_next()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-branch-default");
        var definitionId = await Publish(admin, "默认臂直接汇合", BranchModel());

        var starter = await ClientFor(f, "wf-branch-default");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId, variablesJson = """{"amount":10}""" });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        // 命中 armLow(默认臂,Next==null):不经过任何臂内节点,待办直接落在 merge-approve
        // ——臂 Next 为 null 不等于实例完结。
        var todo = await TodoFor(starter, instanceId);
        Assert.Equal("merge-approve", todo.GetProperty("nodeId").GetString());

        var approve = await PostEnvelope(starter, "/api/v1/workflow/task/approve",
            new { taskId = todo.GetProperty("taskId").GetInt64() });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Gateway_taken_history_records_arm_choice_and_branch_node_enter_leave_once_each()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-branch-hist");
        var definitionId = await Publish(admin, "分支历史", BranchModel());
        var starter = await ClientFor(f, "wf-branch-hist");

        var highStart = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId, variablesJson = """{"amount":200}""" });
        Assert.Equal(0, highStart.GetProperty("code").GetInt32());
        var highInstanceId = highStart.GetProperty("data").GetProperty("instanceId").GetInt64();
        await AssertGatewayTakenAndBranchNodeVisitedOnce(starter, highInstanceId, armId: "armHigh", isDefault: false);

        var lowStart = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId, variablesJson = """{"amount":10}""" });
        Assert.Equal(0, lowStart.GetProperty("code").GetInt32());
        var lowInstanceId = lowStart.GetProperty("data").GetProperty("instanceId").GetInt64();
        await AssertGatewayTakenAndBranchNodeVisitedOnce(starter, lowInstanceId, armId: "armLow", isDefault: true);
    }

    [Fact]
    public async Task Todo_page_resolves_node_name_per_row_when_two_definitions_share_arm_node_id()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-branch-cross-def");
        var definitionIdA = await Publish(admin, "跨定义A", BranchModelWithArmNodeName("A审批节点"));
        var definitionIdB = await Publish(admin, "跨定义B", BranchModelWithArmNodeName("B审批节点"));

        var starter = await ClientFor(f, "wf-branch-cross-def");
        var startA = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId = definitionIdA, variablesJson = """{"amount":200}""" });
        Assert.Equal(0, startA.GetProperty("code").GetInt32());
        var instanceIdA = startA.GetProperty("data").GetProperty("instanceId").GetInt64();

        var startB = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId = definitionIdB, variablesJson = """{"amount":200}""" });
        Assert.Equal(0, startB.GetProperty("code").GetInt32());
        var instanceIdB = startB.GetProperty("data").GetProperty("instanceId").GetInt64();

        // 两个不同定义的臂内节点都叫 "high-approve",但 name 不同——待办页跨定义同页出现时,
        // 每行必须按自己的 definitionVersionId 解析 nodeName,不能共用同一份模型索引缓存。
        var todoA = await TodoFor(starter, instanceIdA);
        var todoB = await TodoFor(starter, instanceIdB);

        Assert.Equal("high-approve", todoA.GetProperty("nodeId").GetString());
        Assert.Equal("A审批节点", todoA.GetProperty("nodeName").GetString());
        Assert.Equal("high-approve", todoB.GetProperty("nodeId").GetString());
        Assert.Equal("B审批节点", todoB.GetProperty("nodeName").GetString());
    }

    /// <summary>与 <see cref="BranchModel"/> 结构相同,仅臂内审批节点的 name 可自定义(跨定义撞 Id 用例专用)。</summary>
    private static object BranchModelWithArmNodeName(string armNodeName) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "branch1",
                type = "branch",
                name = "分支",
                conditions = new object[]
                {
                    new
                    {
                        id = "armHigh",
                        name = "大额",
                        isDefault = false,
                        expr = new { field = "amount", op = "gt", value = 100 },
                        next = new
                        {
                            id = "high-approve",
                            type = "approval",
                            name = armNodeName,
                            props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                            next = (object?)null,
                        },
                    },
                    new { id = "armLow", name = "默认", isDefault = true },
                },
                next = ApprovalNode("merge-approve"),
            },
        },
    };

    /// <summary>
    /// 断言 wf_history 里 branch1 节点的 GatewayTaken(payload armId/isDefault)与 NodeEnter/NodeLeave
    /// 各恰好一条(步骤 4 的判据)。
    /// </summary>
    private static async Task AssertGatewayTakenAndBranchNodeVisitedOnce(
        HttpClient client, long instanceId, string armId, bool isDefault)
    {
        var history = await GetEnvelope(client, $"/api/v1/workflow/instance/history/{instanceId}");
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        var events = history.GetProperty("data").EnumerateArray().ToList();

        var gateway = Assert.Single(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.GatewayTaken &&
            e.GetProperty("nodeId").GetString() == "branch1");
        using var payload = JsonDocument.Parse(gateway.GetProperty("payloadJson").GetString()!);
        Assert.Equal(armId, payload.RootElement.GetProperty("armId").GetString());
        Assert.Equal(isDefault, payload.RootElement.GetProperty("isDefault").GetBoolean());

        Assert.Single(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.NodeEnter &&
            e.GetProperty("nodeId").GetString() == "branch1");
        Assert.Single(events, e =>
            e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.NodeLeave &&
            e.GetProperty("nodeId").GetString() == "branch1");
    }

    private static async Task<JsonElement> TodoFor(HttpClient client, long instanceId)
    {
        var todo = await GetEnvelope(client, "/api/v1/workflow/task/todo?Current=1&Size=20");
        return todo.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("instanceId").GetInt64() == instanceId);
    }

    /// <summary>
    /// root(start) → branch1(armHigh: amount&gt;100 → high-approve;armLow: 默认,无子链) → merge-approve。
    /// <paramref name="mergeApproverId"/> 为空时 merge-approve 沿用 initiator 自批(多数用例够用);
    /// 传入时 merge-approve 改为该专职用户审批,避开 Task 3 相邻节点同人去重的默认行为。
    /// </summary>
    private static object BranchModel(long? mergeApproverId = null) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "branch1",
                type = "branch",
                name = "分支",
                conditions = new object[]
                {
                    new
                    {
                        id = "armHigh",
                        name = "大额",
                        isDefault = false,
                        expr = new { field = "amount", op = "gt", value = 100 },
                        next = ApprovalNode("high-approve"),
                    },
                    new { id = "armLow", name = "默认", isDefault = true },
                },
                next = ApprovalNode("merge-approve", mergeApproverId),
            },
        },
    };

    private static object ApprovalNode(string id, long? userId = null)
    {
        object assignee = userId is { } uid
            ? new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { uid } } }
            : new { provider = "initiator", @params = new { } };
        return new
        {
            id,
            type = "approval",
            name = id,
            props = new { assignee, mode = "any" },
            next = (object?)null,
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

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
