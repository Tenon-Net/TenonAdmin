using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c Task 3「实例完结时间」契约测试(数据库评审 §4.2、§九 #4)。
/// <c>wf_instance.CompletedTime</c> 在实例进入 Approved/Rejected/Cancelled 时与状态<b>同一条 UPDATE</b> 写入,
/// 运行中的实例保持空;加列之前就已终态的旧行由 <c>WfCompletedTimeBackfill</c> 从
/// <see cref="WfHistoryEventType.InstanceCompleted"/> 事件回填,无事件可依据的保持空。
/// <para><b>⚠ 射程声明</b>:本套件钉的是<b>落点与语义</b>,不是四库 DDL 行为。测试库全是新建的,
/// 「nullable <c>ADD COLUMN</c> 在存量表上的四库表现」不在射程内 —— 那条挂在 Task 8 的四库契约套件上
/// (回填用例可整条复用)。写法照 <see cref="WfVersionCasTests"/>:结果一律<b>直查数据库</b>,
/// 不经 DTO —— 本轮刻意不把该列透出接口。</para>
/// </summary>
public class WfCompletedTimeTests
{
    private const string Password = "Test@123456";

    /// <summary>
    /// 同意到底:审批前实例 Running 且完结时间为<b>空</b>,同意后进 Approved 且完结时间落在发起之后。
    /// 前半段同时是「运行中不写」的钉子(不必另起一个宿主)。
    /// </summary>
    [Fact]
    public async Task Approving_to_the_end_stamps_completed_time()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ct-approve-starter");
        var approverId = await AddUser(admin, "wf-ct-approve-approver");
        var definitionId = await Publish(admin, "完结时间-同意", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-ct-approve-starter");
        var approver = await ClientFor(f, "wf-ct-approve-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var running = await InstanceOf(f, instanceId);
        Assert.Equal(WfInstanceStatus.Running, running.Status);
        Assert.Null(running.CompletedTime);

        var approve = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());

        var done = await InstanceOf(f, instanceId);
        Assert.Equal(WfInstanceStatus.Approved, done.Status);
        Assert.NotNull(done.CompletedTime);
        Assert.True(done.CompletedTime >= done.CreateTime,
            $"完结时间 {done.CompletedTime} 不该早于发起时间 {done.CreateTime}");
    }

    /// <summary>拒绝且节点未配路由 → 终止分支,完结时间写入。</summary>
    [Fact]
    public async Task Terminating_reject_stamps_completed_time()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ct-reject-starter");
        var approverId = await AddUser(admin, "wf-ct-reject-approver");
        var definitionId = await Publish(admin, "完结时间-拒绝终止", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-ct-reject-starter");
        var approver = await ClientFor(f, "wf-ct-reject-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new { taskId });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());

        var done = await InstanceOf(f, instanceId);
        Assert.Equal(WfInstanceStatus.Rejected, done.Status);
        Assert.NotNull(done.CompletedTime);
    }

    /// <summary>
    /// <b>只在终止分支写</b>的钉子:<c>onReject = toNode</c> 的拒绝会回退到目标节点、实例仍 Running,
    /// 完结时间必须仍为空。把写入从终止分支挪到拒绝入口(或挪进 <c>CompleteTaskOp</c> 开头),这条立刻红。
    /// </summary>
    [Fact]
    public async Task Reject_routed_to_a_node_leaves_completed_time_null()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ct-tonode-starter");
        var aId = await AddUser(admin, "wf-ct-tonode-a");
        var bId = await AddUser(admin, "wf-ct-tonode-b");
        var definitionId = await Publish(admin, "完结时间-拒绝回退", RejectRouteModel(aId, bId));

        var starter = await ClientFor(f, "wf-ct-tonode-starter");
        var a = await ClientFor(f, "wf-ct-tonode-a");
        var b = await ClientFor(f, "wf-ct-tonode-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var firstTaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = firstTaskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        var secondTaskId = approve.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(b, "/api/v1/workflow/task/reject", new { taskId = secondTaskId });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());

        var routed = await InstanceOf(f, instanceId);
        Assert.Equal(WfInstanceStatus.Running, routed.Status);
        Assert.Null(routed.CompletedTime);
    }

    /// <summary>撤销 → Cancelled 分支同样写入完结时间。</summary>
    [Fact]
    public async Task Cancelling_stamps_completed_time()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ct-cancel-starter");
        var approverId = await AddUser(admin, "wf-ct-cancel-approver");
        var definitionId = await Publish(admin, "完结时间-撤销", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-ct-cancel-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(0, cancel.GetProperty("code").GetInt32());

        var done = await InstanceOf(f, instanceId);
        Assert.Equal(WfInstanceStatus.Cancelled, done.Status);
        Assert.NotNull(done.CompletedTime);
    }

    /// <summary>
    /// 升级回填(评审 §九 #4):手工造两条「加列前就已终态」的旧行 —— 一条有
    /// <see cref="WfHistoryEventType.InstanceCompleted"/> 事件、一条没有 —— 跑一遍回填服务,
    /// 有事件的按事件时间补齐、没事件的<b>保持空</b>;再跑一遍结果不变(幂等)。
    /// <para>事件时间刻意取整秒:MySQL 的 <c>datetime</c> 默认零精度,带毫秒的期望值会在方言层被截断,
    /// 那种红是假的。</para>
    /// </summary>
    [Fact]
    public async Task Backfill_fills_legacy_rows_from_the_completed_event_and_is_idempotent()
    {
        using var f = new WorkflowAppFactory();
        _ = await ClientFor(f, "superAdmin");   // 触发宿主启动 + 建表

        var completedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var withEvent = await InsertLegacyInstanceAsync(f, completedAt);
        var withoutEvent = await InsertLegacyInstanceAsync(f, null);

        await RunBackfillAsync(f);

        Assert.Equal(completedAt, (await InstanceOf(f, withEvent)).CompletedTime);
        Assert.Null((await InstanceOf(f, withoutEvent)).CompletedTime);

        await RunBackfillAsync(f);

        Assert.Equal(completedAt, (await InstanceOf(f, withEvent)).CompletedTime);
        Assert.Null((await InstanceOf(f, withoutEvent)).CompletedTime);
    }

    // ── 辅助 ──

    /// <summary>
    /// 造一条「已终态但完结时间为空」的旧实例;<paramref name="completedEventTime"/> 非空时附一条
    /// <c>InstanceCompleted</c> 历史(回填的唯一依据)。
    /// </summary>
    private static async Task<long> InsertLegacyInstanceAsync(WorkflowAppFactory f, DateTime? completedEventTime)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var instance = new WfInstance
        {
            DefinitionVersionId = 1,
            StarterUserId = 1,
            Status = WfInstanceStatus.Approved,
            CompletedTime = null,
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        if (completedEventTime is { } at)
        {
            await db.Insertable(new WfHistory
            {
                InstanceId = instance.Id,
                EventType = WfHistoryEventType.InstanceCompleted,
                CreateTime = at,                                  // 显式给值 → 审计 AOP 不覆盖
                PayloadJson = "{\"status\":\"Approved\"}",
            }).ExecuteCommandAsync();
        }

        return instance.Id;
    }

    /// <summary>
    /// 手动跑一遍回填服务。它是 <c>internal</c>(不是扩展点,没必要为测试放开),故按类型名从已注册的
    /// <see cref="IHostedService"/> 里取;取不到就是被改名或漏注册了,断言当场报出来。
    /// </summary>
    private static async Task RunBackfillAsync(WorkflowAppFactory f)
    {
        var backfill = f.Services.GetServices<IHostedService>()
            .FirstOrDefault(s => s.GetType().Name == "WfCompletedTimeBackfill");
        Assert.NotNull(backfill);
        await backfill.StartAsync(CancellationToken.None);
    }

    private static async Task<WfInstance> InstanceOf(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId)
            .FirstAsync();
        Assert.NotNull(instance);
        return instance;
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

    /// <summary>两节点链,第二个节点 <c>onReject = toNode</c> 回退到第一个节点(拒绝不终止实例)。</summary>
    private static object RejectRouteModel(long aUserId, long bUserId) => new
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
