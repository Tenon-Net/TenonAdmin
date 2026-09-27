using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>T14 拿回：资格、下游动作窗口、回退重入、历史和回执。</summary>
public class WfTakeBackTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Take_back_reenters_the_approved_node_and_replays_without_duplicate_history()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f, "POST:/api/v1/workflow/task/take-back");
        await AddUser(admin, "wf-takeback-starter");
        var aId = await AddUser(admin, "wf-takeback-a");
        var bId = await AddUser(admin, "wf-takeback-b");
        var definitionId = await Publish(admin, "拿回-回执", Model(aId, bId));
        var starter = await ClientFor(f, "wf-takeback-starter");
        var a = await ClientFor(f, "wf-takeback-a");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var firstTaskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var approved = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = firstTaskId });
        var downstreamTaskId = approved.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        long tokenVersionBefore;
        using (var setupScope = f.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            var downstreamTask = await setupDb.Queryable<WfTask>().Where(t => t.Id == downstreamTaskId).FirstAsync();
            Assert.NotNull(downstreamTask);
            tokenVersionBefore = (await setupDb.Queryable<WfToken>()
                .Where(token => token.Id == downstreamTask!.TokenId).FirstAsync())!.Version;
            await setupDb.Insertable(new WfHisTask
            {
                InstanceId = instanceId,
                NodeId = "other-token-node",
                TokenId = downstreamTask.TokenId + 1,
                UserId = aId,
                Action = WfTaskAction.Approve,
            }).ExecuteCommandAsync();
        }
        var detail = await GetEnvelope(a, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(downstreamTaskId, detail.GetProperty("data").GetProperty("myTakeBackTaskId").GetInt64());

        var first = await PostEnvelope(a, "/api/v1/workflow/task/take-back",
            new { taskId = downstreamTaskId, comment = "请重新核对", requestId = "wf-takeback-replay" });
        var replay = await PostEnvelope(a, "/api/v1/workflow/task/take-back",
            new { taskId = downstreamTaskId, comment = "请重新核对", requestId = "wf-takeback-replay" });
        var conflict = await PostEnvelope(a, "/api/v1/workflow/task/take-back",
            new { taskId = downstreamTaskId, comment = "换一个意见", requestId = "wf-takeback-replay" });

        Assert.Equal(0, first.GetProperty("code").GetInt32());
        Assert.Equal(0, replay.GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.RequestPayloadConflict, conflict.GetProperty("code").GetInt32());
        Assert.Equal(first.GetProperty("data").GetRawText(), replay.GetProperty("data").GetRawText());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var currentTaskId = first.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var currentTask = await db.Queryable<WfTask>().Where(t => t.Id == currentTaskId).FirstAsync();
        Assert.NotNull(currentTask);
        Assert.Equal("approval-a", currentTask!.NodeId);
        Assert.NotEqual(downstreamTaskId, currentTask.Id);
        Assert.Equal(tokenVersionBefore + 2,
            (await db.Queryable<WfToken>().Where(token => token.Id == currentTask.TokenId).FirstAsync())!.Version);
        Assert.Equal(WfInstanceStatus.Running, (await db.Queryable<WfInstance>().InSingleAsync(instanceId))!.Status);
        Assert.Equal(1, await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.TakeBack).CountAsync());

        var oldActors = await db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == downstreamTaskId).ToListAsync();
        Assert.All(oldActors, actor => Assert.Equal(WfActorStatus.Skipped, actor.Status));
        Assert.Equal(1, await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.TakeBack).CountAsync());
    }

    [Fact]
    public async Task Concurrent_take_back_allows_exactly_one_request()
    {
        var notifier = new CountingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = services => services.Replace(
                ServiceDescriptor.Singleton<IWorkflowNotifier>(notifier)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f, "POST:/api/v1/workflow/task/take-back");
        await AddUser(admin, "wf-takeback-race-starter");
        var aId = await AddUser(admin, "wf-takeback-race-a");
        var bId = await AddUser(admin, "wf-takeback-race-b");
        var definitionId = await Publish(admin, "拿回-并发竞争", Model(aId, bId));
        var starter = await ClientFor(f, "wf-takeback-race-starter");
        var a1 = await ClientFor(f, "wf-takeback-race-a");
        var a2 = await ClientFor(f, "wf-takeback-race-a");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var firstTaskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var approved = await PostEnvelope(a1, "/api/v1/workflow/task/approve", new { taskId = firstTaskId });
        var downstreamTaskId = approved.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var results = await Task.WhenAll(
            PostEnvelope(a1, "/api/v1/workflow/task/take-back",
                new { taskId = downstreamTaskId, requestId = "wf-takeback-race-1" }),
            PostEnvelope(a2, "/api/v1/workflow/task/take-back",
                new { taskId = downstreamTaskId, requestId = "wf-takeback-race-2" }));

        var success = Assert.Single(results, result => result.GetProperty("code").GetInt32() == 0);
        Assert.Single(results, result =>
            result.GetProperty("code").GetInt32() == WorkflowErrorCode.TaskConflict);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        Assert.Equal(1, await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.TakeBack).CountAsync());
        Assert.Equal(1, await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.TakeBack).CountAsync());
        var targetTasks = await db.Queryable<WfTask>()
            .Where(t => t.InstanceId == instanceId && t.NodeId == "approval-a").ToListAsync();
        var targetTask = Assert.Single(targetTasks);
        Assert.Equal(success.GetProperty("data").GetProperty("createdTaskId").GetInt64(), targetTask.Id);
        Assert.NotEqual(firstTaskId, targetTask.Id);
        Assert.Equal(1, notifier.TaskRecalledCalls);
    }

    [Fact]
    public async Task Take_back_rejects_without_own_approval_and_after_a_downstream_action()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f,
            "POST:/api/v1/workflow/task/take-back",
            "POST:/api/v1/workflow/task/add-sign");
        await AddUser(admin, "wf-takeback-invalid-starter");
        var aId = await AddUser(admin, "wf-takeback-invalid-a");
        var bId = await AddUser(admin, "wf-takeback-invalid-b");
        var cId = await AddUser(admin, "wf-takeback-invalid-c");
        var definitionId = await Publish(admin, "拿回-窗口", Model(aId, bId));
        var starter = await ClientFor(f, "wf-takeback-invalid-starter");
        var a = await ClientFor(f, "wf-takeback-invalid-a");
        var b = await ClientFor(f, "wf-takeback-invalid-b");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var firstTaskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var approved = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = firstTaskId });
        var downstreamTaskId = approved.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var noApproval = await PostEnvelope(b, "/api/v1/workflow/task/take-back",
            new { taskId = downstreamTaskId, requestId = "wf-takeback-no-approval" });
        Assert.Equal(WorkflowErrorCode.TakeBackNotAllowed, noApproval.GetProperty("code").GetInt32());

        var added = await PostEnvelope(b, "/api/v1/workflow/task/add-sign",
            new { taskId = downstreamTaskId, toUserId = cId, requestId = "wf-takeback-add-sign" });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var afterAction = await PostEnvelope(a, "/api/v1/workflow/task/take-back",
            new { taskId = downstreamTaskId, requestId = "wf-takeback-after-action" });
        Assert.Equal(WorkflowErrorCode.TakeBackNotAllowed, afterAction.GetProperty("code").GetInt32());
    }

    private static object Model(long a, long b) => new
    {
        version = 1,
        root = new
        {
            id = "start", type = "start", name = "发起",
            next = new
            {
                id = "approval-a", type = "approval", name = "一级审批",
                props = new { assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { a } } } },
                next = new
                {
                    id = "approval-b", type = "approval", name = "二级审批",
                    props = new { assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { b } } } },
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
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account, password = Password, name = account, enabled = true, orgId = 1, roleIds = new[] { 2L },
        });
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

    private static async Task GrantPermissions(WorkflowAppFactory f, params string[] permissions)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var menuIds = await db.Queryable<SysMenu>()
            .Where(m => permissions.Contains(m.Permission))
            .Select(m => m.Id)
            .ToListAsync();
        foreach (var menuId in menuIds)
            await db.Insertable(new SysRoleMenu { RoleId = 2, MenuId = menuId }).ExecuteCommandAsync();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private sealed class CountingWorkflowNotifier : IWorkflowNotifier
    {
        private int taskRecalledCalls;

        public int TaskRecalledCalls => Volatile.Read(ref taskRecalledCalls);

        public Task TaskAssignedAsync(
            WfNotifyContext ctx, IReadOnlyList<long> userIds, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InstanceCompletedAsync(WfNotifyContext ctx, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task TaskUrgedAsync(
            WfNotifyContext ctx,
            long taskId,
            long? fromUserId,
            IReadOnlyList<long> toUserIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TaskRecalledAsync(
            WfNotifyContext ctx,
            long taskId,
            IReadOnlyList<long> userIds,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref taskRecalledCalls);
            return Task.CompletedTask;
        }
    }
}
