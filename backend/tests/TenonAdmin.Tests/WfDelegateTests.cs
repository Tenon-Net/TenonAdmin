using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 6「委托(Delegate,一次性)」契约测试。
/// 委托 = 当前办理人把**这一件**待办指给别人代办:同一个 <c>wf_task</c> 换 actor,不推进 token、
/// 不重置耗时/超时基准,<c>wf_his_task</c> 记 <see cref="WfTaskAction.Delegate"/> 与转办区分开。
/// 实例发起人无权委托他人的待办;允许链式委托、不设次数上限(见台账 `## Plan` 必答问题二)。
/// </summary>
public class WfDelegateTests
{
    private const string Password = "Test@123456";

    /// <summary>
    /// 主路径:A 把待办委托给 B。钉四件事——同一个 taskId 换到 B 名下(不是新建待办)、A 不再有待办、
    /// <c>wf_his_task</c> 记的是 <c>Delegate</c> 而不是 <c>Transfer</c>、被委托人能接着正常办完。
    /// </summary>
    [Fact]
    public async Task Pending_approver_can_delegate_todo_to_another_user()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-deleg-ok-starter");
        var aId = await AddUser(admin, "wf-deleg-ok-a");
        var bId = await AddUser(admin, "wf-deleg-ok-b");
        var definitionId = await Publish(admin, "委托-主路径", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-deleg-ok-starter");
        var a = await ClientFor(f, "wf-deleg-ok-a");
        var b = await ClientFor(f, "wf-deleg-ok-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Single(await TodoItemsFor(a, instanceId));

        var delegated = await PostEnvelope(a, "/api/v1/workflow/task/delegate",
            new { taskId, toUserId = bId, comment = "出差,请代办" });
        Assert.Equal(0, delegated.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            delegated.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.Equal(new[] { bId }, delegated.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToArray());

        Assert.Empty(await TodoItemsFor(a, instanceId));
        var bTodo = Assert.Single(await TodoItemsFor(b, instanceId));
        Assert.Equal(taskId, bTodo.GetProperty("taskId").GetInt64());

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var hisTask = Assert.Single(detail.GetProperty("data").GetProperty("hisTasks").EnumerateArray().ToList(),
            h => h.GetProperty("userId").GetInt64() == aId);
        Assert.Equal((int)WfTaskAction.Delegate, hisTask.GetProperty("action").GetInt32());
        Assert.Equal(bId, hisTask.GetProperty("transferToUserId").GetInt64());
        Assert.Equal("出差,请代办", hisTask.GetProperty("comment").GetString());

        // 事件流里的动作标签必须与 wf_his_task 一致(两处走同一个钩子,漏一处就自相矛盾)。
        Assert.Equal("Delegate", await LastTaskCompletedAction(starter, instanceId));

        var approve = await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    /// <summary>
    /// 权限钉子:实例发起人不能把**别人的**待办委托出去(否则等于自选审批人,RBAC/主管链解析全部作废)。
    /// 台账 `## 语义契约` 的「发起人/办理人」按「委托发起人=当前办理人」解读,这条钉住该解读。
    /// </summary>
    [Fact]
    public async Task Starter_cannot_delegate_others_todo()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-deleg-perm-starter");
        var aId = await AddUser(admin, "wf-deleg-perm-a");
        var dId = await AddUser(admin, "wf-deleg-perm-d");
        var definitionId = await Publish(admin, "委托-发起人无权", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-deleg-perm-starter");
        var a = await ClientFor(f, "wf-deleg-perm-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var delegated = await PostEnvelope(starter, "/api/v1/workflow/task/delegate",
            new { taskId, toUserId = dId });
        Assert.Equal(WorkflowErrorCode.TaskConflict, delegated.GetProperty("code").GetInt32());

        // 被拒之后 A 的待办必须原封不动(不能出现"没委托成功但 actor 已被动过"的中间态)。
        Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Empty(await TodoItemsFor(await ClientFor(f, "wf-deleg-perm-d"), instanceId));
    }

    /// <summary>
    /// 目标已是本待办的办理人(会签同节点另一位)→ 拒绝。用的必须是委托自己的
    /// <see cref="WorkflowErrorCode.DelegateTargetInvalid"/>(48026)而不是转办的 48010——错误只返数字码,
    /// 前端按 <c>error.code.&lt;数字&gt;</c> 翻译,复用转办码会弹出「转办目标非法」的错文案。
    /// </summary>
    [Fact]
    public async Task Delegate_to_existing_actor_is_rejected()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-deleg-dup-starter");
        var aId = await AddUser(admin, "wf-deleg-dup-a");
        var bId = await AddUser(admin, "wf-deleg-dup-b");
        var definitionId = await Publish(admin, "委托-目标已是办理人", AllSignModel(aId, bId));

        var starter = await ClientFor(f, "wf-deleg-dup-starter");
        var a = await ClientFor(f, "wf-deleg-dup-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var delegated = await PostEnvelope(a, "/api/v1/workflow/task/delegate",
            new { taskId, toUserId = bId });
        Assert.Equal(WorkflowErrorCode.DelegateTargetInvalid, delegated.GetProperty("code").GetInt32());
        Assert.Equal("alreadyActor", delegated.GetProperty("args").GetProperty("reason").GetString());
    }

    /// <summary>
    /// 链式委托(A→B→C)是**定案允许**的,不是漏做限制:被委托人拿到的是普通 Pending actor,不带
    /// 「你是被委托来的」标记位,所有办理人动词对他一视同仁。
    /// <para><b>「不设次数/深度上限」这条定案的唯一安全依据就钉在本用例的第三跳上</b>:C 再委托回 A 或 B
    /// 一律被 <c>alreadyActor</c> 拦下——因为
    /// <c>TransferTaskOp</c> 那条「目标已是本待办任一 Approver」查询**只看 actor 行存在性、不看
    /// <see cref="WfActorStatus"/></c>,被委托走的人 actor 行仍在(只是翻成 <c>Skipped</c>)。
    /// 链长因此 ≤ 本待办参与过的人数,环路走不成,无界增长不存在,所以才不需要次数上限。
    /// 若有人给该查询加上 <c>Status != Skipped</c>(看着像修「B 收到误委托后不能还给 A」的合理 bug 修复),
    /// A→B→A→B→… 立刻变成无界循环、每跳往永不清理的 <c>wf_his_task</c> 插一行 —— 本段断言就是那道闸门。</para>
    /// </summary>
    [Fact]
    public async Task Delegate_chain_hands_todo_along_without_limit()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-deleg-chain-starter");
        var aId = await AddUser(admin, "wf-deleg-chain-a");
        var bId = await AddUser(admin, "wf-deleg-chain-b");
        var cId = await AddUser(admin, "wf-deleg-chain-c");
        var definitionId = await Publish(admin, "委托-链式两跳", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-deleg-chain-starter");
        var a = await ClientFor(f, "wf-deleg-chain-a");
        var b = await ClientFor(f, "wf-deleg-chain-b");
        var c = await ClientFor(f, "wf-deleg-chain-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var first = await PostEnvelope(a, "/api/v1/workflow/task/delegate", new { taskId, toUserId = bId });
        Assert.Equal(0, first.GetProperty("code").GetInt32());

        var second = await PostEnvelope(b, "/api/v1/workflow/task/delegate", new { taskId, toUserId = cId });
        Assert.Equal(0, second.GetProperty("code").GetInt32());

        Assert.Empty(await TodoItemsFor(a, instanceId));
        Assert.Empty(await TodoItemsFor(b, instanceId));
        var cTodo = Assert.Single(await TodoItemsFor(c, instanceId));
        Assert.Equal(taskId, cTodo.GetProperty("taskId").GetInt64());

        Assert.Equal(2, await HisTaskCount(f, instanceId, WfTaskAction.Delegate));

        // 第三跳:C 想把待办委托回链上任何一位前手,两个方向都必须撞 alreadyActor。
        foreach (var backTo in new[] { aId, bId })
        {
            var bounced = await PostEnvelope(c, "/api/v1/workflow/task/delegate",
                new { taskId, toUserId = backTo });
            Assert.Equal(WorkflowErrorCode.DelegateTargetInvalid, bounced.GetProperty("code").GetInt32());
            Assert.Equal("alreadyActor", bounced.GetProperty("args").GetProperty("reason").GetString());
        }

        // 拒绝后无中间态:待办还在 C 手里,前手没被重新唤醒,也没多出委托历史行。
        var cTodoAfter = Assert.Single(await TodoItemsFor(c, instanceId));
        Assert.Equal(taskId, cTodoAfter.GetProperty("taskId").GetInt64());
        Assert.Empty(await TodoItemsFor(a, instanceId));
        Assert.Empty(await TodoItemsFor(b, instanceId));
        Assert.Equal(2, await HisTaskCount(f, instanceId, WfTaskAction.Delegate));
    }

    /// <summary>
    /// 目标非法的另两条路径:委托给自己(人员选择器里误点极常见)与委托给不存在 / 已停用的用户。
    /// 三者都必须返 <see cref="WorkflowErrorCode.DelegateTargetInvalid"/>(48026),**不是**转办的 48010——
    /// 错误只返数字码、前端按 <c>error.code.&lt;数字&gt;</c> 翻译,复用转办码会让委托失败弹出
    /// 「转办目标无效」的错文案。`TransferTaskOp` 里三处 <c>TargetInvalidErrorCode</c> 钩子,
    /// <see cref="Delegate_to_existing_actor_is_rejected"/> 只钉住 <c>alreadyActor</c> 那一处,本用例补另两处。
    /// </summary>
    [Fact]
    public async Task Delegate_to_self_or_unavailable_target_is_rejected()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-deleg-bad-starter");
        var aId = await AddUser(admin, "wf-deleg-bad-a");
        var disabledId = await AddUser(admin, "wf-deleg-bad-disabled", enabled: false);
        var definitionId = await Publish(admin, "委托-目标非法", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-deleg-bad-starter");
        var a = await ClientFor(f, "wf-deleg-bad-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 委托给自己:走 ToUserId == UserId 那条早退,无 reason。
        var toSelf = await PostEnvelope(a, "/api/v1/workflow/task/delegate", new { taskId, toUserId = aId });
        Assert.Equal(WorkflowErrorCode.DelegateTargetInvalid, toSelf.GetProperty("code").GetInt32());
        Assert.False(toSelf.GetProperty("args").TryGetProperty("reason", out _));

        // 不存在的用户 与 已停用的用户:同一条 userUnavailable 路径。
        foreach (var badId in new[] { 9_999_999L, disabledId })
        {
            var rejected = await PostEnvelope(a, "/api/v1/workflow/task/delegate",
                new { taskId, toUserId = badId });
            Assert.Equal(WorkflowErrorCode.DelegateTargetInvalid, rejected.GetProperty("code").GetInt32());
            Assert.Equal("userUnavailable", rejected.GetProperty("args").GetProperty("reason").GetString());
        }

        // 三次失败都不能留下痕迹:A 的待办原封不动,零委托历史行。
        var aTodo = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal(taskId, aTodo.GetProperty("taskId").GetInt64());
        Assert.Equal(0, await HisTaskCount(f, instanceId, WfTaskAction.Delegate));
    }

    /// <summary>
    /// 陷阱反向钉子:委托<b>不是</b>向后跳转,<see cref="WfTaskAction.Delegate"/> 不能进
    /// <c>EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync</c> 的跳转下界白名单。
    /// <c>start→node1(all,[A,B])→node2(any,[A])</c>:A 在 node1 同意(会签未满票,任务仍开着),B 把待办
    /// 委托给 C,C 同意 → node1 满票进 node2,去重基线应是 node1 的 <c>{A,C}</c> ⊇ node2 的 <c>[A]</c>
    /// → node2 整节点自动通过、实例 <c>Approved</c>。若把 Delegate 当成跳转下界,那条 Delegate 行会把
    /// A 的 Approve 行砍出窗口 → 基线只剩 <c>{C}</c> → A 在 node2 拿到真待办、实例仍 <c>Running</c>。
    /// </summary>
    [Fact]
    public async Task Delegate_row_does_not_reset_adjacent_dedup_baseline()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-deleg-dedup-starter");
        var aId = await AddUser(admin, "wf-deleg-dedup-a");
        var bId = await AddUser(admin, "wf-deleg-dedup-b");
        var cId = await AddUser(admin, "wf-deleg-dedup-c");
        var definitionId = await Publish(admin, "委托-不动去重基线", AllSignThenSameApproverModel(aId, bId));

        var starter = await ClientFor(f, "wf-deleg-dedup-starter");
        var a = await ClientFor(f, "wf-deleg-dedup-a");
        var b = await ClientFor(f, "wf-deleg-dedup-b");
        var c = await ClientFor(f, "wf-deleg-dedup-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approveA = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approveA.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            approveA.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var delegated = await PostEnvelope(b, "/api/v1/workflow/task/delegate", new { taskId, toUserId = cId });
        Assert.Equal(0, delegated.GetProperty("code").GetInt32());

        var approveC = await PostEnvelope(c, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approveC.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approveC.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.Empty(await TodoItemsFor(a, instanceId));
    }

    /// <summary>start → node1(any,[A]) → null。</summary>
    private static object SingleApprovalModel(long aUserId) => new
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
                next = (object?)null,
            },
        },
    };

    /// <summary>start → node1(<b>all</b>,[A,B]) → null。</summary>
    private static object AllSignModel(long aUserId, long bUserId) => new
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
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId, bUserId } },
                    },
                    mode = "all",
                },
                next = (object?)null,
            },
        },
    };

    /// <summary>start → node1(<b>all</b>,[A,B]) → node2(any,[<b>A</b>]) → null;node2 故意复用 node1 的办理人。</summary>
    private static object AllSignThenSameApproverModel(long aUserId, long bUserId) => new
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
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId, bUserId } },
                    },
                    mode = "all",
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
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId } },
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

    private static async Task<long> AddUser(HttpClient admin, string account, bool enabled = true)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = enabled,
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

    /// <summary>本实例 <c>wf_his_task</c> 里某个动作的行数(链式委托的跳数)。</summary>
    private static async Task<int> HisTaskCount(WorkflowAppFactory f, long instanceId, WfTaskAction action)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == action)
            .CountAsync();
    }

    /// <summary>最近一条 <see cref="WfHistoryEventType.TaskCompleted"/> 事件载荷里的 <c>action</c> 标签。</summary>
    private static async Task<string?> LastTaskCompletedAction(HttpClient viewer, long instanceId)
    {
        var history = await GetEnvelope(viewer, $"/api/v1/workflow/instance/history/{instanceId}");
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        var completed = history.GetProperty("data").EnumerateArray()
            .Where(e => e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.TaskCompleted)
            .ToList();
        using var payload = JsonDocument.Parse(completed[^1].GetProperty("payloadJson").GetString()!);
        return payload.RootElement.GetProperty("action").GetString();
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
