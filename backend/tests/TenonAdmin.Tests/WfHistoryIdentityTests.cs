using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M3a-1 Task 1「<c>wf_history</c> 行身份」契约测试:<see cref="WfHistory.Sequence"/> 本实例内严格递增、
/// 从 1 起、无重复;<see cref="WfHistory.ActorType"/>/<see cref="WfHistory.ActorUserId"/> 回答「谁触发的」;
/// <see cref="WfHistory.TokenId"/> 是产生本事件时的活跃 token;<see cref="WfHistory.PayloadVersion"/> 本轮
/// 恒为 1。
/// <para>断言一律**直接查库**(照 <see cref="WfHistoryRequestIdTests"/> 先例),本轮不把这些列透出到 DTO。</para>
/// </summary>
public class WfHistoryIdentityTests
{
    private const string Password = "Test@123456";

    /// <summary>一个实例的 <c>Sequence</c> 从 1 起、严格递增、无重复。</summary>
    [Fact]
    public async Task Sequence_starts_at_one_strictly_increases_and_never_repeats()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hid-seq-starter");
        var aId = await AddUser(admin, "wf-hid-seq-a");
        var bId = await AddUser(admin, "wf-hid-seq-b");
        var definitionId = await Publish(admin, "身份-序号基本", ChainModel(aId, bId));

        var starter = await ClientFor(f, "wf-hid-seq-starter");
        var a = await ClientFor(f, "wf-hid-seq-a");
        var b = await ClientFor(f, "wf-hid-seq-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal(0, (await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId = task2Id }))
            .GetProperty("code").GetInt32());

        var rows = await HistoryOf(f, instanceId);
        var sequences = rows.Select(h => h.Sequence).ToList();
        Assert.Equal(1, sequences.Min());
        Assert.Equal(sequences.Distinct().Count(), sequences.Count);
        Assert.Equal(sequences.OrderBy(x => x).ToList(), sequences);
        for (var i = 1; i < sequences.Count; i++)
            Assert.True(sequences[i] > sequences[i - 1]);
    }

    /// <summary>一次命令写的 N 条历史占 N 个连续号。</summary>
    [Fact]
    public async Task One_commands_rows_occupy_consecutive_numbers()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hid-consec-starter");
        var aId = await AddUser(admin, "wf-hid-consec-a");
        var definitionId = await Publish(admin, "身份-连续号", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hid-consec-starter");
        var a = await ClientFor(f, "wf-hid-consec-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var beforeIds = (await HistoryOf(f, instanceId)).Select(h => h.Id).ToHashSet();

        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        var written = (await HistoryOf(f, instanceId))
            .Where(h => !beforeIds.Contains(h.Id))
            .OrderBy(h => h.Sequence)
            .ToList();
        Assert.True(written.Count > 1, "同意会关闭最后一个节点并完结实例,至少应该写多条历史。");
        for (var i = 1; i < written.Count; i++)
            Assert.Equal(written[i - 1].Sequence + 1, written[i].Sequence);
    }

    /// <summary>两个实例的序号互相独立(各自从 1 起)。</summary>
    [Fact]
    public async Task Two_instances_have_independent_sequences()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hid-indep-starter");
        var aId = await AddUser(admin, "wf-hid-indep-a");
        var definitionId = await Publish(admin, "身份-序号独立", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hid-indep-starter");

        var start1 = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instance1Id = start1.GetProperty("data").GetProperty("instanceId").GetInt64();
        var start2 = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instance2Id = start2.GetProperty("data").GetProperty("instanceId").GetInt64();

        var rows1 = await HistoryOf(f, instance1Id);
        var rows2 = await HistoryOf(f, instance2Id);
        Assert.Equal(1, rows1.OrderBy(h => h.Sequence).First().Sequence);
        Assert.Equal(1, rows2.OrderBy(h => h.Sequence).First().Sequence);
    }

    /// <summary>超时 Job 写的行也有序号,且接在引擎写的行后面。</summary>
    [Fact]
    public async Task Timeout_jobs_rows_also_carry_a_sequence_after_the_engines_rows()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hid-timeoutseq-starter");
        var aId = await AddUser(admin, "wf-hid-timeoutseq-a");
        var definitionId = await Publish(
            admin, "身份-超时序号", SingleApprovalModel(aId, new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, "wf-hid-timeoutseq-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var beforeMax = (await HistoryOf(f, instanceId)).Max(h => h.Sequence);

        await ExpireDueTime(f, instanceId);
        await RunTimeoutJob(f);

        var rows = await HistoryOf(f, instanceId);
        var fired = rows.Where(h => h.EventType == WfHistoryEventType.TimeoutFired).ToList();
        Assert.NotEmpty(fired);
        Assert.All(fired, h => Assert.True(h.Sequence > beforeMax));
    }

    /// <summary>催办行:<c>ActorType == Human</c> 且 <c>ActorUserId ==</c> 催办人。</summary>
    [Fact]
    public async Task Urge_row_has_human_actor_type_and_the_urging_user()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-hid-urge-starter");
        var aId = await AddUser(admin, "wf-hid-urge-a");
        var definitionId = await Publish(admin, "身份-催办", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hid-urge-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId }))
            .GetProperty("code").GetInt32());

        var rows = await HistoryOf(f, instanceId);
        var urged = Assert.Single(rows, h => h.EventType == WfHistoryEventType.TaskUrged);
        Assert.Equal(WfHistoryActorType.Human, urged.ActorType);
        Assert.Equal(starterId, urged.ActorUserId);
    }

    /// <summary>
    /// 超时行(Job 三处的自身写入 + 引擎 <c>BeginTimeoutAsync</c> 那条命令写的全部行):
    /// <c>ActorType == Timeout</c> 且 <c>ActorUserId == null</c>。
    /// </summary>
    [Fact]
    public async Task Timeout_rows_have_timeout_actor_type_and_no_user()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hid-timeoutactor-starter");
        var aId = await AddUser(admin, "wf-hid-timeoutactor-a");
        var definitionId = await Publish(
            admin, "身份-超时行为者", SingleApprovalModel(aId, new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, "wf-hid-timeoutactor-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var beforeIds = (await HistoryOf(f, instanceId)).Select(h => h.Id).ToHashSet();

        await ExpireDueTime(f, instanceId);
        await RunTimeoutJob(f);

        var written = (await HistoryOf(f, instanceId)).Where(h => !beforeIds.Contains(h.Id)).ToList();
        Assert.NotEmpty(written);
        Assert.All(written, h =>
        {
            Assert.Equal(WfHistoryActorType.Timeout, h.ActorType);
            Assert.Null(h.ActorUserId);
        });
    }

    /// <summary>
    /// 用户命令写的每一行:<c>ActorType == Human</c> 且 <c>ActorUserId ==</c> 该次动作的用户
    /// (发起→发起人、同意→审批人、撤销→发起人),含 <c>InstanceStarted</c>。
    /// </summary>
    [Fact]
    public async Task Human_command_rows_carry_the_acting_user()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-hid-human-starter");
        var aId = await AddUser(admin, "wf-hid-human-a");
        var definitionId = await Publish(admin, "身份-人工行为者", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hid-human-starter");
        var a = await ClientFor(f, "wf-hid-human-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var startedRows = await HistoryOf(f, instanceId);
        var started = Assert.Single(startedRows, h => h.EventType == WfHistoryEventType.InstanceStarted);
        Assert.Equal(WfHistoryActorType.Human, started.ActorType);
        Assert.Equal(starterId, started.ActorUserId);

        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        var afterApprove = await HistoryOf(f, instanceId);
        var approveRows = afterApprove.Where(h => h.EventType == WfHistoryEventType.TaskCompleted).ToList();
        Assert.NotEmpty(approveRows);
        Assert.All(approveRows, h =>
        {
            Assert.Equal(WfHistoryActorType.Human, h.ActorType);
            Assert.Equal(aId, h.ActorUserId);
        });

        // 撤销:另开一个实例,发起后不给任何人批,直接撤销——校验 Cancel 命令的行为者是发起人。
        var start2 = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instance2Id = start2.GetProperty("data").GetProperty("instanceId").GetInt64();
        Assert.Equal(0, (await PostEnvelope(starter, "/api/v1/workflow/instance/cancel",
                new { instanceId = instance2Id }))
            .GetProperty("code").GetInt32());
        var cancelRows = (await HistoryOf(f, instance2Id))
            .Where(h => h.EventType == WfHistoryEventType.InstanceCompleted)
            .ToList();
        Assert.NotEmpty(cancelRows);
        Assert.All(cancelRows, h =>
        {
            Assert.Equal(WfHistoryActorType.Human, h.ActorType);
            Assert.Equal(starterId, h.ActorUserId);
        });
    }

    /// <summary>
    /// 所有行 <c>PayloadVersion == 1</c>,<c>TokenId</c> 非空且等于当时的活跃 token
    /// (单一审批链下 token 实体 Id 全程不变,只有 NodeId/NodeVisitId 随访问更新)。
    /// </summary>
    [Fact]
    public async Task All_rows_have_payload_version_one_and_a_non_null_token_id()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hid-payload-starter");
        var aId = await AddUser(admin, "wf-hid-payload-a");
        var bId = await AddUser(admin, "wf-hid-payload-b");
        var definitionId = await Publish(admin, "身份-载荷版本", ChainModel(aId, bId));

        var starter = await ClientFor(f, "wf-hid-payload-starter");
        var a = await ClientFor(f, "wf-hid-payload-a");
        var b = await ClientFor(f, "wf-hid-payload-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var task1Id = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve1 = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = task1Id });
        var task2Id = approve1.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal(0, (await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId = task2Id }))
            .GetProperty("code").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var token = await db.Queryable<WfToken>().Where(t => t.InstanceId == instanceId).FirstAsync();
        Assert.NotNull(token);

        var rows = await HistoryOf(f, instanceId);
        Assert.NotEmpty(rows);
        Assert.All(rows, h =>
        {
            Assert.Equal(1, h.PayloadVersion);
            Assert.NotNull(h.TokenId);
            Assert.Equal(token.Id, h.TokenId);
        });
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

    /// <summary>start → node1(any,[userId],可选 timeout) → null。</summary>
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
