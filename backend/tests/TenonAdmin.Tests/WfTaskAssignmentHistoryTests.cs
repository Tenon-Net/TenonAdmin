using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c 与 M3a 之间的过渡步骤「办理人分配历史 + 耗时口径」契约测试(数据库评审 §4.3、§4.4)。
/// 二选一定案:<b>保留 <c>wf_task_actor</c>,任务关闭只把状态翻终态,不再物理删</b>——不新建
/// <c>wf_task_assignment_history</c> 表。理由与全部读路径的核对写在 <see cref="CompleteTaskOp"/>.
/// <c>CloseTaskAsync</c> 的注释里;本套件钉的是行为契约本身:关闭后行还在、状态对、
/// <c>DurationMs</c>/<c>StartedTime</c> 改用 <c>ActivatedTime</c> 而不是 <c>WfTask.CreateTime</c>。
/// </summary>
public class WfTaskAssignmentHistoryTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Any_mode_retains_non_acting_candidates_as_skipped_after_close()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hist-any-starter");
        var aId = await AddUser(admin, "wf-hist-any-a");
        var bId = await AddUser(admin, "wf-hist-any-b");
        var cId = await AddUser(admin, "wf-hist-any-c");
        var definitionId = await Publish(admin, "分配历史-或签", AnyApprovalModel(aId, bId, cId));

        var starter = await ClientFor(f, "wf-hist-any-starter");
        var a = await ClientFor(f, "wf-hist-any-a");

        var instanceId = await Start(starter, definitionId);
        var taskId = await PendingTaskIdFor(f, instanceId);

        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());

        var actors = await ActorsOf(f, taskId);
        Assert.Equal(3, actors.Count);
        Assert.Equal(WfActorStatus.Done, actors.Single(x => x.UserId == aId).Status);
        Assert.Equal(WfActorStatus.Skipped, actors.Single(x => x.UserId == bId).Status);
        Assert.Equal(WfActorStatus.Skipped, actors.Single(x => x.UserId == cId).Status);
    }

    /// <summary>
    /// 顺序会签第二位被晋级时才写 <c>ActivatedTime</c>,晋级前的等待不该算进第二位的办理耗时——把
    /// <c>WfTask.CreateTime</c> 手工推远(模拟建任务后过了很久),第二位仍在晋级后很快同意,断言
    /// <c>DurationMs</c> 是小值(反映刚晋级到同意的耗时),不是大到接近推远的那个跨度(旧公式会算出的值)。
    /// </summary>
    [Fact]
    public async Task Sequential_promotion_stamps_activated_time_and_duration_excludes_earlier_wait()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hist-seq-starter");
        var aId = await AddUser(admin, "wf-hist-seq-a");
        var bId = await AddUser(admin, "wf-hist-seq-b");
        var definitionId = await Publish(admin, "分配历史-顺序", SequentialApprovalModel(aId, bId));

        var starter = await ClientFor(f, "wf-hist-seq-starter");
        var a = await ClientFor(f, "wf-hist-seq-a");
        var b = await ClientFor(f, "wf-hist-seq-b");

        var instanceId = await Start(starter, definitionId);
        var taskId = await PendingTaskIdFor(f, instanceId);

        // 把任务的创建时间推远到 2 小时前——旧公式(now - Task.CreateTime)会把这 2 小时也算进第二位的耗时。
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Updateable<WfTask>()
                .SetColumns(t => new WfTask { CreateTime = DateTime.Now.AddHours(-2) })
                .Where(t => t.Id == taskId)
                .ExecuteCommandAsync();
        }

        var approveA = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approveA.GetProperty("code").GetInt32());

        // a 同意晋级 b 的这一刻用的是真实时钟(现在),与被推远的 Task.CreateTime 无关。
        var bActor = (await ActorsOf(f, taskId)).Single(x => x.UserId == bId);
        Assert.Equal(WfActorStatus.Pending, bActor.Status);
        Assert.NotNull(bActor.ActivatedTime);
        Assert.True((DateTime.Now - bActor.ActivatedTime!.Value).TotalSeconds < 30);

        var approveB = await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approveB.GetProperty("code").GetInt32());

        var hisB = await LastHisTaskFor(f, instanceId, bId, WfTaskAction.Approve);
        Assert.NotNull(hisB.StartedTime);
        // 旧公式会得到约 7,200,000ms(2 小时);新公式应远小于此,证明用的是 ActivatedTime 不是 Task.CreateTime。
        Assert.True(hisB.DurationMs < 60_000,
            $"DurationMs={hisB.DurationMs} 应反映刚晋级到同意的耗时,不该混入被推远的 2 小时等待");
    }

    [Fact]
    public async Task Sequential_candidate_never_reached_is_skipped_not_stuck_waiting_when_task_closes_by_reject()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hist-seqwait-starter");
        var aId = await AddUser(admin, "wf-hist-seqwait-a");
        var bId = await AddUser(admin, "wf-hist-seqwait-b");
        var cId = await AddUser(admin, "wf-hist-seqwait-c");
        var definitionId = await Publish(
            admin, "分配历史-顺序未轮到", SequentialApprovalModel(aId, bId, cId));

        var starter = await ClientFor(f, "wf-hist-seqwait-starter");
        var a = await ClientFor(f, "wf-hist-seqwait-a");
        var b = await ClientFor(f, "wf-hist-seqwait-b");

        var instanceId = await Start(starter, definitionId);
        var taskId = await PendingTaskIdFor(f, instanceId);

        var approveA = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approveA.GetProperty("code").GetInt32());
        // c 此刻仍是 Waiting——b 还没轮到,c 更没轮到。
        Assert.Equal(WfActorStatus.Waiting, (await ActorsOf(f, taskId)).Single(x => x.UserId == cId).Status);

        var rejectB = await PostEnvelope(b, "/api/v1/workflow/task/reject", new { taskId });
        Assert.Equal(0, rejectB.GetProperty("code").GetInt32());

        var actors = await ActorsOf(f, taskId);
        Assert.Equal(3, actors.Count);
        Assert.Equal(WfActorStatus.Done, actors.Single(x => x.UserId == aId).Status);
        Assert.Equal(WfActorStatus.Done, actors.Single(x => x.UserId == bId).Status);
        // c 从未被晋级过,任务被 b 拒绝关闭——必须落终态 Skipped,不能永远卡在 Waiting。
        Assert.Equal(WfActorStatus.Skipped, actors.Single(x => x.UserId == cId).Status);
    }

    [Fact]
    public async Task Cancel_retains_all_actors_as_skipped_including_never_reached_candidate()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hist-cancel-starter");
        var aId = await AddUser(admin, "wf-hist-cancel-a");
        var bId = await AddUser(admin, "wf-hist-cancel-b");
        var definitionId = await Publish(admin, "分配历史-撤销", SequentialApprovalModel(aId, bId));

        var starter = await ClientFor(f, "wf-hist-cancel-starter");

        var instanceId = await Start(starter, definitionId);
        var taskId = await PendingTaskIdFor(f, instanceId);

        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(0, cancel.GetProperty("code").GetInt32());

        var actors = await ActorsOf(f, taskId);
        Assert.Equal(2, actors.Count);
        // a 是 Pending 被撤销打断,b 从未晋级(Waiting)——两者都要落 Skipped,都不能物理消失。
        Assert.Equal(WfActorStatus.Skipped, actors.Single(x => x.UserId == aId).Status);
        Assert.Equal(WfActorStatus.Skipped, actors.Single(x => x.UserId == bId).Status);
    }

    [Fact]
    public async Task Transfer_retains_the_original_actor_as_skipped_after_the_task_eventually_closes()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-hist-transfer-starter");
        var aId = await AddUser(admin, "wf-hist-transfer-a");
        var bId = await AddUser(admin, "wf-hist-transfer-b");
        var definitionId = await Publish(admin, "分配历史-转办", AnyApprovalModel(aId));

        var starter = await ClientFor(f, "wf-hist-transfer-starter");
        var a = await ClientFor(f, "wf-hist-transfer-a");
        var b = await ClientFor(f, "wf-hist-transfer-b");

        var instanceId = await Start(starter, definitionId);
        var taskId = await PendingTaskIdFor(f, instanceId);

        var transfer = await PostEnvelope(
            a, "/api/v1/workflow/task/transfer", new { taskId, toUserId = bId });
        Assert.Equal(0, transfer.GetProperty("code").GetInt32());

        var afterTransfer = await ActorsOf(f, taskId);
        var bActor = afterTransfer.Single(x => x.UserId == bId);
        Assert.Equal(WfActorStatus.Pending, bActor.Status);
        Assert.NotNull(bActor.ActivatedTime);

        var approveB = await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approveB.GetProperty("code").GetInt32());

        var actors = await ActorsOf(f, taskId);
        Assert.Equal(2, actors.Count);
        Assert.Equal(WfActorStatus.Skipped, actors.Single(x => x.UserId == aId).Status);
        Assert.Equal(WfActorStatus.Done, actors.Single(x => x.UserId == bId).Status);
    }

    // ── 模型 ──

    /// <summary>start → approve-1(any,[userIds]) → null。</summary>
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

    /// <summary>start → approve-1(seq,[userIds]) → null。</summary>
    private static object SequentialApprovalModel(params long[] userIds) => new
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
                    mode = "seq",
                },
                next = (object?)null,
            },
        },
    };

    // ── DB 直读 ──

    private static async Task<long> PendingTaskIdFor(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var task = await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).FirstAsync();
        Assert.NotNull(task);
        return task.Id;
    }

    private static async Task<List<WfTaskActor>> ActorsOf(WorkflowAppFactory f, long taskId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfTaskActor>().Where(a => a.TaskId == taskId).ToListAsync();
    }

    private static async Task<WfHisTask> LastHisTaskFor(
        WorkflowAppFactory f, long instanceId, long userId, WfTaskAction action)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var row = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.UserId == userId && h.Action == action)
            .OrderBy(h => h.Id, OrderByType.Desc)
            .FirstAsync();
        Assert.NotNull(row);
        return row;
    }

    // ── 脚手架(与 WfVersionCasTests 同款) ──

    private static async Task<long> Start(HttpClient starter, long definitionId)
    {
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        return start.GetProperty("data").GetProperty("instanceId").GetInt64();
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
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<System.Text.Json.JsonElement> PostEnvelope(
        HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
