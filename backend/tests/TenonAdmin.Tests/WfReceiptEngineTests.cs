using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c Task 5「引擎写路径接幂等回执」契约测试。挂钩点是 <see cref="WorkflowEngine.ExecuteAsync"/> 里
/// <c>UseTranAsync</c> 的开头:命中回执直接返回**第一次**的 <see cref="WfEngineResult"/>,否则跑完 Op 链
/// 再同事务回填。
/// <para><b>⚠ 射程声明,先说清免得被误读</b>:<b>真实并发的交错构造不出来,本文件钉的是重试而不是竞态。</b>
/// 原因与 <see cref="WfVersionCasTests"/> 的射程声明逐字同型 —— 单线程顺序执行下,第二次请求读到的必然是
/// 第一次**已提交**的回执,那正是「HTTP 响应丢了、客户端重发」这个真实场景,也是本 Task 要解决的问题。
/// 而「两个请求同时进来、一个撞唯一索引」需要 A 查 → B 插并提交 → A 插这个交错;起真线程在 SQLite 上只会
/// 得到一个随机红的用例,不是钉子。<c>TryBeginAsync</c> 的唯一冲突分支已由 Task 2 的
/// <c>WfOperationReceiptTests</c> 覆盖,它在四库上的方言差异(尤其 PostgreSQL 冲突后整事务 aborted)
/// 属 Task 8,见台账 `## Findings` 的 P2→Task 8。</para>
/// </summary>
public class WfReceiptEngineTests
{
    private const string Password = "Test@123456";

    /// <summary>
    /// 串行双提交:同一 key 发两次 approve → 第二次返回**同一个** `instanceId`,且**只推进一次**
    /// (`wf_his_task` 仍只有一行)。这是本 Task 的核心钉子 —— 去掉命中后的短路 `return`,它立刻红。
    /// </summary>
    [Fact]
    public async Task Same_request_id_replays_the_first_result_without_advancing_twice()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-replay-starter");
        var aId = await AddUser(admin, "wf-rcp-replay-a");
        var bId = await AddUser(admin, "wf-rcp-replay-b");
        var definitionId = await Publish(admin, "回执-串行重放", ChainModel(aId, bId));

        var starter = await ClientFor(f, "wf-rcp-replay-starter");
        var a = await ClientFor(f, "wf-rcp-replay-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var first = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-replay-001" });
        Assert.Equal(0, first.GetProperty("code").GetInt32());

        var second = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-replay-001" });
        Assert.Equal(0, second.GetProperty("code").GetInt32());

        // 结果逐字一致:第二次拿到的是第一次的快照,不是一次新的推进。
        Assert.Equal(
            first.GetProperty("data").GetProperty("instanceId").GetInt64(),
            second.GetProperty("data").GetProperty("instanceId").GetInt64());
        Assert.Equal(
            first.GetProperty("data").GetProperty("createdTaskId").GetInt64(),
            second.GetProperty("data").GetProperty("createdTaskId").GetInt64());

        // 真正的证据在库里:只审过一次。少了这条,「短路返回了正确的 JSON」和「又推进了一次但结果碰巧一样」
        // 分不开。
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(1, await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve)
            .CountAsync());
    }

    /// <summary>
    /// 终态重试:实例已完结后同 key 再发 → 返回第一次的结果(`instanceStatus = Approved`),
    /// **不是** `TaskConflict`。这正是台账 `## DONE-CONDITION` 那条「不再只报冲突码当丢响应重试的唯一出口」。
    /// </summary>
    [Fact]
    public async Task Retry_after_the_instance_finished_returns_the_first_result_not_a_conflict()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-final-starter");
        var aId = await AddUser(admin, "wf-rcp-final-a");
        var definitionId = await Publish(admin, "回执-终态重试", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-rcp-final-starter");
        var a = await ClientFor(f, "wf-rcp-final-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var first = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-final-001" });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            first.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var retry = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-final-001" });
        Assert.Equal(0, retry.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            retry.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    /// <summary>
    /// 同一 key、同一待办,但**动作不同**(先同意后拒绝)→ 不命中。
    /// 这条钉的是 `CompleteTaskCmd` 必须按 `Action` 拆成两个 `CommandType`:不拆的话拒绝会命中同意的回执,
    /// 拿到 `code = 0` 和一份「同意成功」的结果 —— 用户点了拒绝,系统回他同意了。
    /// </summary>
    [Fact]
    public async Task Same_key_with_a_different_action_is_not_a_hit()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-action-starter");
        var aId = await AddUser(admin, "wf-rcp-action-a");
        var definitionId = await Publish(admin, "回执-同key不同动作", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-rcp-action-starter");
        var a = await ClientFor(f, "wf-rcp-action-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-action-001" });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());

        // 待办已随同意关闭,所以拒绝按正常业务规则失败。**关键是它没有被幂等成功**。
        var reject = await PostEnvelope(
            a, "/api/v1/workflow/task/reject", new { taskId, requestId = "req-action-001" });
        Assert.NotEqual(0, reject.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// 不带 `requestId` → **一行回执都不建**,行为与接回执之前完全一致(第二次撞业务规则)。
    /// 资格判断若从 `{ RequestId: not null }` 放宽成 `is WfWriteCmd`,这条立刻红。
    /// </summary>
    [Fact]
    public async Task A_request_without_a_key_creates_no_receipt_at_all()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-nokey-starter");
        var aId = await AddUser(admin, "wf-rcp-nokey-a");
        var definitionId = await Publish(admin, "回执-无key", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-rcp-nokey-starter");
        var a = await ClientFor(f, "wf-rcp-nokey-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());
        Assert.NotEqual(0, (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        Assert.Equal(0, await ReceiptCount(f));
    }

    /// <summary>
    /// 超时自动通过 → **不建回执**。<see cref="TimeoutFireCmd"/> 不继承 <see cref="WfWriteCmd"/>,
    /// 系统扫出来的动作没有「用户这一次点击」的身份;这条钉住那个类型判断是真的生效的。
    /// </summary>
    [Fact]
    public async Task A_timeout_fire_creates_no_receipt()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-timeout-starter");
        var aId = await AddUser(admin, "wf-rcp-timeout-a");
        var definitionId = await Publish(
            admin, "回执-超时", SingleApprovalModel(aId, new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, "wf-rcp-timeout-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        // 到期靠直接把 DueTime 推到过去造(与 WfTimeoutTests 同一姿势),不拨全局时钟:
        // hours = 0 只让 DueTime 落在"现在",而判据是不等式,边界上不保证命中。
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var past = DateTime.Now - TimeSpan.FromHours(1);
            var affected = await db.Updateable<WfTask>()
                .SetColumns(t => new WfTask { DueTime = past })
                .Where(t => t.InstanceId == instanceId)
                .ExecuteCommandAsync();
            Assert.True(affected > 0, "没有活跃待办可推到期——测试前置条件坏了。");
        }

        await RunTimeoutJob(f);

        // 超时真的跑了(否则「0 行回执」是因为什么都没发生,钉子空转)。
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var status = await db.Queryable<WfInstance>()
                .Where(i => i.Id == instanceId).Select(i => i.Status).FirstAsync();
            Assert.Equal(WfInstanceStatus.Approved, status);
        }

        Assert.Equal(0, await ReceiptCount(f));
    }

    /// <summary>
    /// 业务失败 → 事务回滚,**回执不残留**;同 key 再发仍报同一个业务码,不会被幂等成「成功」。
    /// 若把 `CommitAsync` 挪到事务之外,占位行就会留在库里,第二次重试拿到「有回执但没结果」——这条会红。
    /// </summary>
    [Fact]
    public async Task A_failed_command_leaves_no_receipt_and_stays_failed_on_retry()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-fail-starter");
        var aId = await AddUser(admin, "wf-rcp-fail-a");
        var definitionId = await Publish(admin, "回执-业务失败", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-rcp-fail-starter");
        var a = await ClientFor(f, "wf-rcp-fail-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 发起人先撤销(无人已批,允许);此后审批必然失败。
        Assert.Equal(0, (await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId }))
            .GetProperty("code").GetInt32());

        var failed = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-fail-001" });
        var code = failed.GetProperty("code").GetInt32();
        Assert.NotEqual(0, code);

        Assert.Equal(0, await ReceiptCount(f));

        var again = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-fail-001" });
        Assert.Equal(code, again.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// 同一个 key 被**不同的人、在不同的待办上**使用 → 两条独立的回执,各自正常推进。
    /// identity 里的 `ActorUserId` 与 `TargetId` 若漏掉任一个,B 就会命中 A 的回执,拿到 A 的结果 ——
    /// 那是把两个人的审批混成一次。
    /// <para>这条也是本文件对「并发」能给出的**在射程内**的部分:回执的区分度靠六维,不靠时序。</para>
    /// </summary>
    [Fact]
    public async Task The_same_key_from_a_different_actor_and_target_does_not_collide()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-dim-starter");
        var aId = await AddUser(admin, "wf-rcp-dim-a");
        var bId = await AddUser(admin, "wf-rcp-dim-b");
        var definitionId = await Publish(admin, "回执-六维区分", ChainModel(aId, bId));

        var starter = await ClientFor(f, "wf-rcp-dim-starter");
        var a = await ClientFor(f, "wf-rcp-dim-a");
        var b = await ClientFor(f, "wf-rcp-dim-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var shared = "req-shared-key";
        var first = await PostEnvelope(
            a, "/api/v1/workflow/task/approve", new { taskId = task1Id, requestId = shared });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        var task2Id = first.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal((int)WfInstanceStatus.Running, first.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var second = await PostEnvelope(
            b, "/api/v1/workflow/task/approve", new { taskId = task2Id, requestId = shared });
        Assert.Equal(0, second.GetProperty("code").GetInt32());

        // 没被短路:B 的审批把实例推到了终态,而 A 的回执里存的是 Running。
        Assert.Equal((int)WfInstanceStatus.Approved,
            second.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.Equal(2, await ReceiptCount(f));
    }

    /// <summary>
    /// 落库的六维 + 结果列。回执行是排障时唯一能看的东西,写歪了(比如 `TargetId` 存成实例 Id)
    /// 唯一索引照样工作、用例照样绿,只有直接断言列值才拦得住。
    /// </summary>
    [Fact]
    public async Task The_stored_receipt_carries_the_six_identity_dimensions()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rcp-cols-starter");
        var aId = await AddUser(admin, "wf-rcp-cols-a");
        var definitionId = await Publish(admin, "回执-列值", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-rcp-cols-starter");
        var a = await ClientFor(f, "wf-rcp-cols-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 首尾空白由 Task 4 的命令层 Trim 掉,落库的必须是 Trim 之后的值(诊断列与 hash 同源)。
        Assert.Equal(0, (await PostEnvelope(
                a, "/api/v1/workflow/task/approve", new { taskId, requestId = "  req-cols-001  " }))
            .GetProperty("code").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var receipt = await db.Queryable<WfOperationReceipt>().FirstAsync();

        Assert.Equal(WfCommandType.Approve, receipt.CommandType);
        Assert.Equal(WfTargetType.Task, receipt.TargetType);
        Assert.Equal(taskId, receipt.TargetId);
        Assert.Equal(aId, receipt.ActorUserId);
        Assert.Equal("req-cols-001", receipt.RequestKey);
        Assert.Equal(WfIdentityHash.ScopeSentinel, receipt.ScopeKey);
        Assert.Equal(0, receipt.ResultCode);
        Assert.False(string.IsNullOrEmpty(receipt.ResultJson));

        // 存的就是那次执行的结果,不是一个占位空壳。
        var restored = JsonSerializer.Deserialize<WfEngineResult>(receipt.ResultJson!, WfModelJson.Options);
        Assert.NotNull(restored);
        Assert.Equal(WfInstanceStatus.Approved, restored.InstanceStatus);
    }

    // ── 辅助 ──

    private static async Task<int> ReceiptCount(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfOperationReceipt>().CountAsync();
    }

    /// <summary>手动触发一次超时扫描——不启调度器(与 <see cref="WfTimeoutTests"/> 同一姿势)。</summary>
    private static async Task RunTimeoutJob(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var job = scope.ServiceProvider.GetServices<IAdminJob>().OfType<WfTimeoutJob>().Single();
        var now = DateTime.Now;
        await job.ExecuteAsync(
            new JobExecutionContext
            {
                JobId = 1,
                JobCode = "wf-timeout-scan",
                JobName = "流程超时扫描",
                FireInstanceId = 1,
                ScheduledTime = now,
                FireTime = now,
                Log = _ => { },
            },
            CancellationToken.None);
    }

    /// <summary>start → node1(any,[user],可选 timeout) → null。</summary>
    private static object SingleApprovalModel(long userId, object? timeout = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new
            {
                provider = "user",
                @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } },
            },
            ["mode"] = "any",
        };
        if (timeout is not null)
            props["timeout"] = timeout;

        return new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = new { id = "node1", type = "approval", name = "node1", props, next = (object?)null },
            },
        };
    }

    /// <summary>start → node1(any,[a]) → node2(any,[b]) → null。</summary>
    private static object ChainModel(long aUserId, long bUserId) => new
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

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
