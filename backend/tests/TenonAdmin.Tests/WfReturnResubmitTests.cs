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

        // 关闭活跃任务是否真的物理清了 wf_task(不止是靠 CAS 认领把办理人自己那行翻 Skipped)。
        // wf_task_actor 不再物理删——数据库评审 §4.4 定案保留为分配历史,关闭只把状态翻终态。
        // token 是否真的回退到 Node 策略配置的目标节点(否则整段 token 回退代码删掉套件也不会红)。
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var remainingActors = await db.Queryable<WfTaskActor>().Where(x => x.TaskId == task2Id).ToListAsync();
            Assert.Single(remainingActors);
            Assert.Equal(WfActorStatus.Skipped, remainingActors[0].Status);
            Assert.Equal(0, await db.Queryable<WfTask>().Where(x => x.Id == task2Id).CountAsync());
        }

        Assert.Equal("node1", await ActiveTokenNodeId(f, instanceId));

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var hisTasks = detail.GetProperty("data").GetProperty("hisTasks").EnumerateArray().ToList();
        Assert.Contains(hisTasks, h =>
            h.GetProperty("userId").GetInt64() == bId && h.GetProperty("action").GetInt32() == (int)WfTaskAction.Return);

        // 历史事件载荷里的目标节点(前端「退回到了哪一步」的唯一数据源)。
        Assert.Equal("node1", await ReturnedTargetNodeId(starter, instanceId));
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
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(a, "/api/v1/workflow/task/return", new { taskId });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            ret.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        // 无先例时优雅退化到 start,而不是留在当前节点或报错。
        Assert.Equal("start", await ActiveTokenNodeId(f, instanceId));
        Assert.Equal("start", await ReturnedTargetNodeId(starter, instanceId));
        Assert.Empty(await TodoItemsFor(a, instanceId));
    }

    /// <summary>
    /// <c>Prev</c> 策略的主路径(有先例):A 批过 node1 后 B 在 node2 退回 → 目标必须是 node1。
    /// 若目标解析恒返回 <c>start</c>(把 <c>ResolveTargetNodeIdAsync</c> 整个替换成 <c>return Root.Id</c>
    /// 的变异),这条会红。
    /// </summary>
    [Fact]
    public async Task Return_with_prev_policy_targets_last_approved_node()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-prevhit-starter");
        var aId = await AddUser(admin, "wf-return-prevhit-a");
        var bId = await AddUser(admin, "wf-return-prevhit-b");
        var definitionId = await Publish(admin, "退回-Prev策略有先例", ReturnPrevPolicyChainModel(aId, bId));

        var starter = await ClientFor(f, "wf-return-prevhit-starter");
        var a = await ClientFor(f, "wf-return-prevhit-a");
        var b = await ClientFor(f, "wf-return-prevhit-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
        Assert.Equal("node1", await ActiveTokenNodeId(f, instanceId));
        Assert.Equal("node1", await ReturnedTargetNodeId(starter, instanceId));
    }

    /// <summary>
    /// 会签(<see cref="WfSignMode.All"/>)下退回:<c>CompleteTaskOp</c> 在计票<b>之前</b>就插 <c>wf_his_task</c>,
    /// 故 B 同意后任务仍开着、当前节点 node2 已经有一条自己的 Approve 行。C 此时调退回,<c>Prev</c> 若不排除
    /// 当前节点自身就会把 node2 解析成目标(历史/UI 显示「退回到了 node2」),语义上应当是 node1。
    /// </summary>
    [Fact]
    public async Task Return_under_all_sign_mode_targets_previous_node_not_current()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-all-starter");
        var aId = await AddUser(admin, "wf-return-all-a");
        var bId = await AddUser(admin, "wf-return-all-b");
        var cId = await AddUser(admin, "wf-return-all-c");
        var definitionId = await Publish(admin, "退回-会签排除本节点", ReturnAllSignModeModel(aId, bId, cId));

        var starter = await ClientFor(f, "wf-return-all-starter");
        var a = await ClientFor(f, "wf-return-all-a");
        var b = await ClientFor(f, "wf-return-all-b");
        var c = await ClientFor(f, "wf-return-all-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 会签第一位同意:node2 还没满票,任务仍开着,但 node2 已经有一条 Approve 行了。
        var approve2 = await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId = task2Id });
        Assert.Equal(0, approve2.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            approve2.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var ret = await PostEnvelope(c, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
        Assert.Equal("node1", await ActiveTokenNodeId(f, instanceId));
        Assert.Equal("node1", await ReturnedTargetNodeId(starter, instanceId));
    }

    /// <summary><c>Any</c> 策略的成功路径:目标是本实例真正走过的节点时放行,token 落到该节点。</summary>
    [Fact]
    public async Task Return_with_any_policy_accepts_walked_target()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-anyok-starter");
        var aId = await AddUser(admin, "wf-return-anyok-a");
        var bId = await AddUser(admin, "wf-return-anyok-b");
        var cId = await AddUser(admin, "wf-return-anyok-c");
        var definitionId = await Publish(admin, "退回-Any策略合法目标", ReturnAnyPolicyModel(aId, bId, cId));

        var starter = await ClientFor(f, "wf-return-anyok-starter");
        var a = await ClientFor(f, "wf-return-anyok-a");
        var b = await ClientFor(f, "wf-return-anyok-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return",
            new { taskId = task2Id, targetNodeId = "node1" });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
        Assert.Equal("node1", await ActiveTokenNodeId(f, instanceId));
        Assert.Equal("node1", await ReturnedTargetNodeId(starter, instanceId));
    }

    /// <summary>节点没开退回(<c>returnPolicy</c> 缺省)→ 48024 且 <c>reason=policyNotConfigured</c>。</summary>
    [Fact]
    public async Task Return_without_policy_is_rejected()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-nopolicy-starter");
        var aId = await AddUser(admin, "wf-return-nopolicy-a");
        var definitionId = await Publish(admin, "退回-未配置策略", NoReturnPolicyModel(aId));

        var starter = await ClientFor(f, "wf-return-nopolicy-starter");
        var a = await ClientFor(f, "wf-return-nopolicy-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(a, "/api/v1/workflow/task/return", new { taskId });
        Assert.Equal(WorkflowErrorCode.ReturnNotAllowed, ret.GetProperty("code").GetInt32());
        Assert.Equal("policyNotConfigured", ret.GetProperty("args").GetProperty("reason").GetString());
    }

    /// <summary>
    /// <c>Node</c> 策略但没配目标节点 → 运行期 48024 且 <c>reason=targetNotConfigured</c>。
    /// 这个组合从 M2b 起<b>发布期就会被拒</b>(见 <c>WfPublishNodeRefValidationTests</c>),所以这里只能靠
    /// 直接篡改已发布快照的 <c>ModelJson</c> 来复现——模拟校验上线之前存量的、或消费者覆写掉发布期校验后
    /// 落库的定义,证明运行期这道防线自己也在。
    /// </summary>
    [Fact]
    public async Task Return_with_node_policy_but_no_target_is_rejected()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-return-notarget-starter");
        var aId = await AddUser(admin, "wf-return-notarget-a");
        var bId = await AddUser(admin, "wf-return-notarget-b");
        var definitionId = await Publish(admin, "退回-Node策略缺目标", ReturnNodePolicyModel(aId, bId));

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var snapshot = await db.Queryable<WfDefinitionVersion>()
                .Where(v => v.DefinitionId == definitionId && v.Version == 1)
                .FirstAsync();
            Assert.NotNull(snapshot);
            var stripped = snapshot.ModelJson.Replace("\"returnToNodeId\":\"node1\"", "\"returnToNodeId\":null");
            Assert.NotEqual(snapshot.ModelJson, stripped);
            await db.Updateable<WfDefinitionVersion>()
                .SetColumns(v => new WfDefinitionVersion { ModelJson = stripped })
                .Where(v => v.Id == snapshot.Id)
                .ExecuteCommandAsync();
        }

        var starter = await ClientFor(f, "wf-return-notarget-starter");
        var a = await ClientFor(f, "wf-return-notarget-a");
        var b = await ClientFor(f, "wf-return-notarget-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(WorkflowErrorCode.ReturnNotAllowed, ret.GetProperty("code").GetInt32());
        Assert.Equal("targetNotConfigured", ret.GetProperty("args").GetProperty("reason").GetString());
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
    /// 三节点链(start→node1[A]→node2[B]→node3[C,returnPolicy=node,returnToNodeId=node1]):验证<b>重提本身</b>
    /// ——复用同一实例行、从 <c>start</c> 整链重走、留下 <see cref="WfHistoryEventType.InstanceResubmitted"/>。
    /// 退回目标跨越两个节点,与「同一人相邻节点去重」的基线判定不重叠,所以这条用例只钉住重提这一件事。
    /// 「退回目标恰好就是紧邻的上一个已审批节点」这条更常见的配置由
    /// <see cref="Resubmit_after_return_to_immediately_previous_node_reassigns_that_nodes_approver"/>
    /// 用两节点链专门钉住(那条锚的是「向后跳转重置去重基线」)。
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
    /// 两节点链(start→node1[A]→node2[B,returnPolicy=node→node1])——「退回上一步」这个最常见的真实配置。
    /// 退回目标 node1 正好是「最近一条 Approve 行」所在的节点,若去重基线不因向后跳转重置,重提从
    /// <c>start</c> 重走时 node1 会被判成「A 已审过」而整节点自动通过,直接落回 node2 → 「从头重走」名存实亡
    /// (审批留痕看起来走了流程,实际跳过了已批节点)。故这里钉的是<b>A 的待办重新出现在 node1</b>。
    /// </summary>
    [Fact]
    public async Task Resubmit_after_return_to_immediately_previous_node_reassigns_that_nodes_approver()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-resubmit-prev-starter");
        var aId = await AddUser(admin, "wf-resubmit-prev-a");
        var bId = await AddUser(admin, "wf-resubmit-prev-b");
        var definitionId = await Publish(admin, "重提-回跳紧邻上一节点", ReturnNodePolicyModel(aId, bId));

        var starter = await ClientFor(f, "wf-resubmit-prev-starter");
        var a = await ClientFor(f, "wf-resubmit-prev-a");
        var b = await ClientFor(f, "wf-resubmit-prev-b");

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
        Assert.Equal(new[] { aId }, resubmit.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToArray());

        var aTodo = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal("node1", aTodo.GetProperty("nodeId").GetString());
        Assert.Empty(await TodoItemsFor(b, instanceId));
    }

    /// <summary>
    /// 重提会把带 cc 节点的整条链重走一遍。<c>wf_cc</c> 没有唯一约束,写入不幂等就会给同一
    /// <c>(instanceId, nodeId, userId)</c> 再插一行 → 抄送列表出现同一单据的重复条目,标已读只标掉其中一行。
    /// </summary>
    [Fact]
    public async Task Resubmit_does_not_duplicate_cc_rows()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-resubmit-cc-starter");
        var ccId = await AddUser(admin, "wf-resubmit-cc-watcher");
        var aId = await AddUser(admin, "wf-resubmit-cc-a");
        var bId = await AddUser(admin, "wf-resubmit-cc-b");
        var definitionId = await Publish(admin, "重提-抄送不重复", ResubmitWithCcModel(ccId, aId, bId));

        var starter = await ClientFor(f, "wf-resubmit-cc-starter");
        var a = await ClientFor(f, "wf-resubmit-cc-a");
        var b = await ClientFor(f, "wf-resubmit-cc-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal(1, await CcRowCount(f, instanceId, "cc1", ccId));

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());

        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new { instanceId });
        Assert.Equal(0, resubmit.GetProperty("code").GetInt32());

        // cc1 被重走了第二遍,但 (instance, cc1, watcher) 只能有一行。
        Assert.Equal(1, await CcRowCount(f, instanceId, "cc1", ccId));
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

    /// <summary>start → cc1(cc,[watcher]) → node1(any,[A]) → node2(any,[B],returnPolicy=node→node1) → null。</summary>
    private static object ResubmitWithCcModel(long ccUserId, long aUserId, long bUserId) => new
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
                name = "cc1",
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
        },
    };

    /// <summary>start → node1(any,[A]) → node2(any,[B],returnPolicy=prev) → null。</summary>
    private static object ReturnPrevPolicyChainModel(long aUserId, long bUserId) => new
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

    /// <summary>start → node1(any,[A]) → node2(<b>all</b>,[B,C],returnPolicy=prev) → null。</summary>
    private static object ReturnAllSignModeModel(long aUserId, long bUserId, long cUserId) => new
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
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { bUserId, cUserId } },
                        },
                        mode = "all",
                        returnPolicy = "prev",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    /// <summary>start → node1(any,[A],<b>不配</b> returnPolicy) → null。</summary>
    private static object NoReturnPolicyModel(long aUserId) => new
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

    /// <summary>本实例当前活跃 token 停在哪个节点(退回的目标解析结果唯一的持久化落点)。</summary>
    private static async Task<string> ActiveTokenNodeId(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == instanceId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        Assert.NotNull(token);
        return token.NodeId;
    }

    /// <summary><see cref="WfHistoryEventType.TaskReturned"/> 事件载荷里的目标节点(前端展示用)。</summary>
    private static async Task<string?> ReturnedTargetNodeId(HttpClient starter, long instanceId)
    {
        var history = await GetEnvelope(starter, $"/api/v1/workflow/instance/history/{instanceId}");
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        var returned = Assert.Single(history.GetProperty("data").EnumerateArray().ToList(),
            e => e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.TaskReturned);
        using var payload = JsonDocument.Parse(returned.GetProperty("payloadJson").GetString()!);
        return payload.RootElement.GetProperty("targetNodeId").GetString();
    }

    private static async Task<int> CcRowCount(WorkflowAppFactory f, long instanceId, string nodeId, long userId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfCc>()
            .Where(c => c.InstanceId == instanceId && c.NodeId == nodeId && c.UserId == userId)
            .CountAsync();
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
