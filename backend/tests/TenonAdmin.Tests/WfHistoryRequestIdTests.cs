using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c Task 6「`wf_history.RequestId`」契约测试。一次用户动作的请求键落进它引发的**每一条**历史事件,
/// 排障时靠它把「一次点击」与整串事件串起来。值与 `wf_operation_receipt.RequestKey` 同源(都来自
/// <see cref="WfWriteCmd.RequestId"/>,归一化全仓只有那一份),两张表各自的既有命名不强行统一。
/// <para>断言一律**直接查库**:本轮刻意不把该列透出到 <c>WfHistoryItemOutput</c>(那是 OpenAPI 变更,归 Task 10)。</para>
/// </summary>
public class WfHistoryRequestIdTests
{
    private const string Password = "Test@123456";

    /// <summary>
    /// 发起带 key → `InstanceStarted` 那一行有值。
    /// <para><b>这条专钉「构造 ctx 时就带上」</b>:该行是在 <c>BeginStartAsync</c> 里、Agenda 还没跑起来时
    /// 写的。任何「等 <c>switch</c> 返回后再往 ctx 上赋值」的实现,别的用例都能绿,**只有它会红** ——
    /// 而它恰恰是排障时最先看的一行。</para>
    /// </summary>
    [Fact]
    public async Task The_instance_started_row_carries_the_key_written_before_the_agenda_runs()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hrid-start-starter");
        var aId = await AddUser(admin, "wf-hrid-start-a");
        var definitionId = await Publish(admin, "历史RequestId-发起", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hrid-start-starter");
        var start = await PostEnvelope(
            starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = "req-hist-start" });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var rows = await HistoryOf(f, instanceId);
        var started = Assert.Single(rows, h => h.EventType == WfHistoryEventType.InstanceStarted);
        Assert.Equal("req-hist-start", started.RequestId);
    }

    /// <summary>
    /// 同意带 key → **这次命令产生的每一条**历史行都带同一个值(不是只有第一条)。
    /// 一次动作会连写好几条(离开节点 / 实例完结 …),漏掉后面几条就等于串不起完整链路。
    /// </summary>
    [Fact]
    public async Task Every_row_written_by_one_command_carries_the_same_key()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hrid-appr-starter");
        var aId = await AddUser(admin, "wf-hrid-appr-a");
        var definitionId = await Publish(admin, "历史RequestId-同意", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hrid-appr-starter");
        var a = await ClientFor(f, "wf-hrid-appr-a");

        // 发起**不带** key,好让接下来的断言只盯审批那次写的行。
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var beforeIds = (await HistoryOf(f, instanceId)).Select(h => h.Id).ToHashSet();

        Assert.Equal(0, (await PostEnvelope(
                a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-hist-approve" }))
            .GetProperty("code").GetInt32());

        var written = (await HistoryOf(f, instanceId)).Where(h => !beforeIds.Contains(h.Id)).ToList();
        Assert.NotEmpty(written);
        Assert.All(written, h => Assert.Equal("req-hist-approve", h.RequestId));
    }

    /// <summary>
    /// 不带 key → 那批行是 <c>null</c>,**不是空串**。两者分不开的话,「客户端没带 key」和
    /// 「带了个空 key」在排障时就是同一个样子,而它们的幂等语义完全不同。
    /// </summary>
    [Fact]
    public async Task Rows_from_a_request_without_a_key_are_null_not_empty()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hrid-nokey-starter");
        var aId = await AddUser(admin, "wf-hrid-nokey-a");
        var definitionId = await Publish(admin, "历史RequestId-无key", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hrid-nokey-starter");
        var a = await ClientFor(f, "wf-hrid-nokey-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        var rows = await HistoryOf(f, instanceId);
        Assert.NotEmpty(rows);
        Assert.All(rows, h => Assert.Null(h.RequestId));
    }

    /// <summary>
    /// 超时触发的那行为 <c>null</c>。<see cref="WfTimeoutJob"/> 绕开执行上下文直插本表,而系统扫出来的
    /// 动作**没有**「用户这一次点击」的身份 —— 这里钉的是「别顺手给它补一个值」。
    /// </summary>
    [Fact]
    public async Task A_timeout_row_has_no_key()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hrid-timeout-starter");
        var aId = await AddUser(admin, "wf-hrid-timeout-a");
        var definitionId = await Publish(
            admin, "历史RequestId-超时", SingleApprovalModel(aId, new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, "wf-hrid-timeout-starter");
        var start = await PostEnvelope(
            starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = "req-hist-timeout" });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        await ExpireDueTime(f, instanceId);
        await RunTimeoutJob(f);

        var rows = await HistoryOf(f, instanceId);
        var fired = Assert.Single(rows, h => h.EventType == WfHistoryEventType.TimeoutFired);
        Assert.Null(fired.RequestId);

        // 同一实例上发起那行**是**有值的 —— 否则「超时行为空」可能只是因为整列压根没写进去。
        Assert.Equal("req-hist-timeout",
            rows.Single(h => h.EventType == WfHistoryEventType.InstanceStarted).RequestId);
    }

    /// <summary>
    /// 催办那行为 <c>null</c>:`UrgeAsync` 不进引擎、控制器刻意不透传 `requestId`(Task 4 G7)。
    /// 这是那条决策在数据层的另一面。
    /// </summary>
    [Fact]
    public async Task An_urge_row_has_no_key()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hrid-urge-starter");
        var aId = await AddUser(admin, "wf-hrid-urge-a");
        var definitionId = await Publish(admin, "历史RequestId-催办", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hrid-urge-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(
                starter, "/api/v1/workflow/task/urge", new { taskId, requestId = "req-hist-urge" }))
            .GetProperty("code").GetInt32());

        var rows = await HistoryOf(f, instanceId);
        var urged = Assert.Single(rows, h => h.EventType == WfHistoryEventType.TaskUrged);
        Assert.Null(urged.RequestId);
    }

    /// <summary>
    /// 同 key 重放 → **一条历史行都不新增**。台账 `## 语义契约` 的「命中回执根本不进引擎」在历史侧的钉子:
    /// 短路发生在命令 `switch` 之前,`AppendHistoryAsync` 一次都跑不到,所以不需要任何去重机制。
    /// </summary>
    [Fact]
    public async Task Replaying_the_same_key_appends_no_history_at_all()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hrid-replay-starter");
        var aId = await AddUser(admin, "wf-hrid-replay-a");
        var bId = await AddUser(admin, "wf-hrid-replay-b");
        var definitionId = await Publish(admin, "历史RequestId-重放", ChainModel(aId, bId));

        var starter = await ClientFor(f, "wf-hrid-replay-starter");
        var a = await ClientFor(f, "wf-hrid-replay-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(
                a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-hist-replay" }))
            .GetProperty("code").GetInt32());
        var afterFirst = (await HistoryOf(f, instanceId)).Count;

        Assert.Equal(0, (await PostEnvelope(
                a, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-hist-replay" }))
            .GetProperty("code").GetInt32());

        Assert.Equal(afterFirst, (await HistoryOf(f, instanceId)).Count);
    }

    // ── 辅助 ──

    private static async Task<List<WfHistory>> HistoryOf(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId)
            .OrderBy(h => h.CreateTime)
            .ToListAsync();
    }

    /// <summary>把活跃待办的 <c>DueTime</c> 推到过去(与 <see cref="WfTimeoutTests"/> 同一姿势,不拨全局时钟)。</summary>
    private static async Task ExpireDueTime(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var past = DateTime.Now - TimeSpan.FromHours(2);
        var affected = await db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { DueTime = past })
            .Where(t => t.InstanceId == instanceId)
            .ExecuteCommandAsync();
        Assert.True(affected > 0, "没有活跃待办可推到期——测试前置条件坏了。");
    }

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
