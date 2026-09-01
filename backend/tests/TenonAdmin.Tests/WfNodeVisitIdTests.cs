using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M3a-1 Task 1「<c>NodeVisitId</c>」契约测试。token 每次经 <see cref="EnterNodeOp"/> 进入新节点时生成一次
/// 雪花 Id,与 <see cref="WfToken.NodeId"/> 同一条 UPDATE 落库;停留期间(未满票的同意、转办、催办)不变;
/// 同一次访问建出的 <c>wf_task</c>/<c>wf_cc</c>/<c>wf_history</c> 行都携带同一个值。
/// <para>断言一律**直接查库**(照 <see cref="WfHistoryRequestIdTests"/> 先例),本轮不把该列透出到 DTO。</para>
/// </summary>
public class WfNodeVisitIdTests
{
    private const string Password = "Test@123456";

    /// <summary>进入节点后 <c>wf_token.NodeVisitId</c> 非空。</summary>
    [Fact]
    public async Task Token_node_visit_id_is_set_after_entering_a_node()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nvid-basic-starter");
        var aId = await AddUser(admin, "wf-nvid-basic-a");
        var definitionId = await Publish(admin, "访问Id-基本", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-nvid-basic-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var token = await ActiveToken(f, instanceId);
        Assert.NotNull(token.NodeVisitId);
        Assert.True(token.NodeVisitId > 0);
    }

    /// <summary>
    /// 同一次访问建的 <c>wf_task</c> / <c>wf_history</c>(NodeEnter+TaskCreated)与 token 相等;
    /// 同一次访问建的 <c>wf_cc</c> 与它自己那次访问的 <c>wf_history</c>(NodeEnter+CcSent)相等——
    /// 抄送节点在发起当次就被走过并继续向前,token 停下来的是后面的审批节点,故不能再拿"当前 token"
    /// 去核对已经走过的抄送节点,只能核对"同一次访问建的几张表互相一致"。
    /// </summary>
    [Fact]
    public async Task Rows_created_in_the_same_visit_share_the_same_value()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nvid-same-starter");
        var watcherId = await AddUser(admin, "wf-nvid-same-watcher");
        var aId = await AddUser(admin, "wf-nvid-same-a");
        var definitionId = await Publish(admin, "访问Id-同一次访问", CcThenApprovalModel(watcherId, aId));

        var starter = await ClientFor(f, "wf-nvid-same-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var token = await ActiveToken(f, instanceId);
        Assert.Equal("node1", token.NodeId);
        Assert.NotNull(token.NodeVisitId);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var task = await db.Queryable<WfTask>().Where(t => t.Id == taskId).FirstAsync();
        Assert.NotNull(task);
        Assert.Equal(token.NodeVisitId, task.NodeVisitId);

        var node1Enter = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.NodeId == "node1" && h.EventType == WfHistoryEventType.NodeEnter)
            .FirstAsync();
        var node1TaskCreated = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.NodeId == "node1" && h.EventType == WfHistoryEventType.TaskCreated)
            .FirstAsync();
        Assert.Equal(token.NodeVisitId, node1Enter.NodeVisitId);
        Assert.Equal(token.NodeVisitId, node1TaskCreated.NodeVisitId);

        var cc1Enter = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.NodeId == "cc1" && h.EventType == WfHistoryEventType.NodeEnter)
            .FirstAsync();
        var cc1Sent = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.NodeId == "cc1" && h.EventType == WfHistoryEventType.CcSent)
            .FirstAsync();
        var ccRow = await db.Queryable<WfCc>()
            .Where(c => c.InstanceId == instanceId && c.NodeId == "cc1" && c.UserId == watcherId)
            .FirstAsync();
        Assert.NotNull(cc1Enter.NodeVisitId);
        Assert.Equal(cc1Enter.NodeVisitId, cc1Sent.NodeVisitId);
        Assert.Equal(cc1Enter.NodeVisitId, ccRow.NodeVisitId);

        // cc1 与 node1 是两次不同的访问。
        Assert.NotEqual(cc1Enter.NodeVisitId, token.NodeVisitId);
    }

    /// <summary>
    /// 头等钉子:同一节点被再次进入(拒绝路由回跳)产生不同的访问 Id,而第一次访问留下的旧行
    /// (第一次的 <c>wf_history</c> NodeEnter)仍保持旧值不被覆盖。
    /// </summary>
    [Fact]
    public async Task Re_entering_the_same_node_produces_a_different_value_and_old_rows_keep_the_old_one()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nvid-reenter-starter");
        var aId = await AddUser(admin, "wf-nvid-reenter-a");
        var bId = await AddUser(admin, "wf-nvid-reenter-b");
        var definitionId = await Publish(admin, "访问Id-再次进入", TwoNodeRejectRouteModel(aId, bId));

        var starter = await ClientFor(f, "wf-nvid-reenter-starter");
        var a = await ClientFor(f, "wf-nvid-reenter-a");
        var b = await ClientFor(f, "wf-nvid-reenter-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var firstEnter = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.NodeId == "node1" && h.EventType == WfHistoryEventType.NodeEnter)
            .FirstAsync();
        var firstVisitId = firstEnter.NodeVisitId;
        Assert.NotNull(firstVisitId);

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(b, "/api/v1/workflow/task/reject", new { taskId = task2Id });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            reject.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var token = await ActiveToken(f, instanceId);
        Assert.Equal("node1", token.NodeId);
        Assert.NotEqual(firstVisitId, token.NodeVisitId);

        // 第一次访问留下的 wf_history 行值不变——不是被覆盖成了新访问的值。
        var firstEnterAgain = await db.Queryable<WfHistory>()
            .Where(h => h.Id == firstEnter.Id)
            .FirstAsync();
        Assert.Equal(firstVisitId, firstEnterAgain.NodeVisitId);

        // 第二次访问建出的新待办携带新值。
        var newTask = await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).FirstAsync();
        Assert.NotNull(newTask);
        Assert.Equal(token.NodeVisitId, newTask.NodeVisitId);
    }

    /// <summary>
    /// 停留期间不变:会签(<see cref="WfSignMode.All"/>)下第一票同意(走 <c>CompleteTaskOp</c> 未满票分支,
    /// 只推进 <c>ClaimTokenAsync</c>)后,token 的 <see cref="WfToken.Version"/> 前进而
    /// <see cref="WfToken.NodeVisitId"/> 保持不变。
    /// </summary>
    [Fact]
    public async Task Node_visit_id_stays_put_while_the_task_remains_open()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nvid-stay-starter");
        var bId = await AddUser(admin, "wf-nvid-stay-b");
        var cId = await AddUser(admin, "wf-nvid-stay-c");
        var definitionId = await Publish(admin, "访问Id-停留不变", AllSignModel(bId, cId));

        var starter = await ClientFor(f, "wf-nvid-stay-starter");
        var b = await ClientFor(f, "wf-nvid-stay-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var before = await ActiveToken(f, instanceId);
        Assert.NotNull(before.NodeVisitId);

        var approve = await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var after = await ActiveToken(f, instanceId);
        Assert.Equal(before.NodeVisitId, after.NodeVisitId);
        Assert.True(after.Version > before.Version);
    }

    /// <summary>
    /// <c>wf_his_task.NodeVisitId</c> 与关闭它的那件待办当次的访问 Id 一致(而非「携带它关闭的那件待办的
    /// 访问 Id」这种更强的说法——当前引擎下写入点与 token 尚未推进重合,无法把「取自 Task」和「取自
    /// ctx.Token」区分开,见 <see cref="WfHisTask.NodeVisitId"/> 注释)。同意 / 转办 / 退回三条路各断言一次,
    /// 合并为一条测试。
    /// </summary>
    [Fact]
    public async Task His_task_visit_id_matches_the_visit_that_created_the_task()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        // 同意
        {
            await AddUser(admin, "wf-nvid-his-appr-starter");
            var aId = await AddUser(admin, "wf-nvid-his-appr-a");
            var definitionId = await Publish(admin, "访问Id-hisTask-同意", SingleApprovalModel(aId));
            var starter = await ClientFor(f, "wf-nvid-his-appr-starter");
            var a = await ClientFor(f, "wf-nvid-his-appr-a");

            var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
            var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
            var expected = (await db.Queryable<WfTask>().Where(t => t.Id == taskId).FirstAsync()).NodeVisitId;

            var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
            Assert.Equal(0, approve.GetProperty("code").GetInt32());

            var hisTask = await db.Queryable<WfHisTask>()
                .Where(h => h.TaskId == taskId && h.Action == WfTaskAction.Approve)
                .FirstAsync();
            Assert.Equal(expected, hisTask.NodeVisitId);
        }

        // 转办
        {
            await AddUser(admin, "wf-nvid-his-xfer-starter");
            var aId = await AddUser(admin, "wf-nvid-his-xfer-a");
            var dId = await AddUser(admin, "wf-nvid-his-xfer-d");
            var definitionId = await Publish(admin, "访问Id-hisTask-转办", SingleApprovalModel(aId));
            var starter = await ClientFor(f, "wf-nvid-his-xfer-starter");
            var a = await ClientFor(f, "wf-nvid-his-xfer-a");

            var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
            var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
            var expected = (await db.Queryable<WfTask>().Where(t => t.Id == taskId).FirstAsync()).NodeVisitId;

            var transfer = await PostEnvelope(a, "/api/v1/workflow/task/transfer", new { taskId, toUserId = dId });
            Assert.Equal(0, transfer.GetProperty("code").GetInt32());

            var hisTask = await db.Queryable<WfHisTask>()
                .Where(h => h.TaskId == taskId && h.Action == WfTaskAction.Transfer)
                .FirstAsync();
            Assert.Equal(expected, hisTask.NodeVisitId);
        }

        // 退回
        {
            await AddUser(admin, "wf-nvid-his-ret-starter");
            var aId = await AddUser(admin, "wf-nvid-his-ret-a");
            var bId = await AddUser(admin, "wf-nvid-his-ret-b");
            var definitionId = await Publish(admin, "访问Id-hisTask-退回", TwoNodeReturnModel(aId, bId));
            var starter = await ClientFor(f, "wf-nvid-his-ret-starter");
            var a = await ClientFor(f, "wf-nvid-his-ret-a");
            var b = await ClientFor(f, "wf-nvid-his-ret-b");

            var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
            var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

            var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
            var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();
            var expected = (await db.Queryable<WfTask>().Where(t => t.Id == task2Id).FirstAsync()).NodeVisitId;

            var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
            Assert.Equal(0, ret.GetProperty("code").GetInt32());

            var hisTask = await db.Queryable<WfHisTask>()
                .Where(h => h.TaskId == task2Id && h.Action == WfTaskAction.Return)
                .FirstAsync();
            Assert.Equal(expected, hisTask.NodeVisitId);
        }
    }

    /// <summary>抄送节点重走(重提)后 <c>wf_cc</c> 不新增行(去重键未变),已有行的访问 Id 保持首次值。</summary>
    [Fact]
    public async Task Cc_row_keeps_its_first_visit_id_across_resubmit()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nvid-ccresubmit-starter");
        var ccId = await AddUser(admin, "wf-nvid-ccresubmit-watcher");
        var aId = await AddUser(admin, "wf-nvid-ccresubmit-a");
        var bId = await AddUser(admin, "wf-nvid-ccresubmit-b");
        var definitionId = await Publish(admin, "访问Id-cc重提不变", CcThenReturnableModel(ccId, aId, bId));

        var starter = await ClientFor(f, "wf-nvid-ccresubmit-starter");
        var a = await ClientFor(f, "wf-nvid-ccresubmit-a");
        var b = await ClientFor(f, "wf-nvid-ccresubmit-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ccBefore = await db.Queryable<WfCc>()
            .Where(c => c.InstanceId == instanceId && c.NodeId == "cc1" && c.UserId == ccId)
            .FirstAsync();
        Assert.NotNull(ccBefore.NodeVisitId);

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        Assert.Equal(0, approve1.GetProperty("code").GetInt32());
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var ret = await PostEnvelope(b, "/api/v1/workflow/task/return", new { taskId = task2Id });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());

        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new { instanceId });
        Assert.Equal(0, resubmit.GetProperty("code").GetInt32());

        var ccRows = await db.Queryable<WfCc>()
            .Where(c => c.InstanceId == instanceId && c.NodeId == "cc1" && c.UserId == ccId)
            .ToListAsync();
        var ccAfter = Assert.Single(ccRows);
        Assert.Equal(ccBefore.NodeVisitId, ccAfter.NodeVisitId);
    }

    /// <summary><c>InstanceStarted</c> 那行 <c>NodeVisitId</c> 为 <c>null</c>(写在 <c>EnterNodeOp</c> 之前),<c>TokenId</c> 非空。</summary>
    [Fact]
    public async Task Instance_started_row_has_no_visit_id_but_has_a_token_id()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nvid-started-starter");
        var aId = await AddUser(admin, "wf-nvid-started-a");
        var definitionId = await Publish(admin, "访问Id-InstanceStarted", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-nvid-started-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var started = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.InstanceStarted)
            .FirstAsync();
        Assert.Null(started.NodeVisitId);
        Assert.NotNull(started.TokenId);

        var token = await ActiveToken(f, instanceId);
        Assert.Equal(token.Id, started.TokenId);
    }

    // ── 模型 ──

    /// <summary>start → node1(any,[userId],可选 timeout) → null。</summary>
    private static object SingleApprovalModel(long userId) => new
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
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    /// <summary>start → cc1(cc,[watcher]) → node1(any,[a]) → null。</summary>
    private static object CcThenApprovalModel(long watcherId, long aUserId) => new
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
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { watcherId } },
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
                    next = (object?)null,
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

    /// <summary>start → node1(all,[b,c]) → null。</summary>
    private static object AllSignModel(long bUserId, long cUserId) => new
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
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { bUserId, cUserId } },
                    },
                    mode = "all",
                },
                next = (object?)null,
            },
        },
    };

    /// <summary>start → node1(any,[A]) → node2(any,[B],returnPolicy=node,returnToNodeId=node1) → null。</summary>
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
                        returnPolicy = "node",
                        returnToNodeId = "node1",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    /// <summary>start → cc1(cc,[watcher]) → node1(any,[a]) → node2(any,[b],returnPolicy=node→node1) → null。</summary>
    private static object CcThenReturnableModel(long ccUserId, long aUserId, long bUserId) => new
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

    // ── 辅助 ──

    private static async Task<WfToken> ActiveToken(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == instanceId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        Assert.NotNull(token);
        return token;
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

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
