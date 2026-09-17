using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>T13 加签/减签纵切：任务 CAS、actor 状态、历史目标和回执重放。</summary>
public class WfSignTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Add_sign_keeps_task_and_replay_does_not_duplicate_history()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantSignPermissions(f);
        await AddUser(admin, "wf-sign-add-starter");
        var aId = await AddUser(admin, "wf-sign-add-a");
        var bId = await AddUser(admin, "wf-sign-add-b");
        var cId = await AddUser(admin, "wf-sign-add-c");
        var definitionId = await Publish(admin, "加签-回执", AllModel(aId, bId));
        var starter = await ClientFor(f, "wf-sign-add-starter");
        var a = await ClientFor(f, "wf-sign-add-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var key = "wf-sign-add-replay";

        var first = await PostEnvelope(a, "/api/v1/workflow/task/add-sign",
            new { taskId, toUserId = cId, comment = "补签", requestId = key });
        var second = await PostEnvelope(a, "/api/v1/workflow/task/add-sign",
            new { taskId, toUserId = cId, comment = "补签", requestId = key });
        var conflict = await PostEnvelope(a, "/api/v1/workflow/task/add-sign",
            new { taskId, toUserId = cId, comment = "不同意见", requestId = key });

        Assert.Equal(0, first.GetProperty("code").GetInt32());
        Assert.Equal(0, second.GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.RequestPayloadConflict, conflict.GetProperty("code").GetInt32());
        Assert.Equal(first.GetProperty("data").GetRawText(), second.GetProperty("data").GetRawText());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        Assert.Equal(1, await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AddSign).CountAsync());
        var row = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AddSign).FirstAsync();
        Assert.Equal(cId, row.TargetUserId);
        var signEvent = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.SignChanged)
            .FirstAsync();
        using var payload = JsonDocument.Parse(signEvent!.PayloadJson!);
        Assert.Equal(2, payload.RootElement.GetProperty("oldDenominator").GetInt32());
        Assert.Equal(3, payload.RootElement.GetProperty("newDenominator").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("oldThreshold").GetInt32());
        Assert.Equal(3, payload.RootElement.GetProperty("newThreshold").GetInt32());
    }

    [Fact]
    public async Task Remove_sign_skips_target_and_allows_remaining_approver_to_finish()
    {
        var notifier = new CountingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = services => services.Replace(
                ServiceDescriptor.Singleton<IWorkflowNotifier>(notifier)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await GrantSignPermissions(f);
        await AddUser(admin, "wf-sign-remove-starter");
        var aId = await AddUser(admin, "wf-sign-remove-a");
        var bId = await AddUser(admin, "wf-sign-remove-b");
        var definitionId = await Publish(admin, "减签-推进", AllModel(aId, bId));
        var starter = await ClientFor(f, "wf-sign-remove-starter");
        var a = await ClientFor(f, "wf-sign-remove-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var removed = await PostEnvelope(a, "/api/v1/workflow/task/remove-sign",
            new { taskId, toUserId = bId, comment = "无需会签", requestId = "wf-sign-remove" });
        Assert.Equal(0, removed.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            removed.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var approved = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal((int)WfInstanceStatus.Approved,
            approved.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var actor = await db.Queryable<WfTaskActor>()
            .Where(x => x.TaskId == taskId && x.UserId == bId).FirstAsync();
        Assert.Equal(WfActorStatus.Skipped, actor.Status);
        var history = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.RemoveSign).FirstAsync();
        Assert.Equal(bId, history.TargetUserId);
        Assert.Equal(1, notifier.TaskSignChangedCalls);
        Assert.Contains(bId, notifier.SignChangedUserIds);
        Assert.Contains(aId, notifier.SignChangedUserIds);
    }

    [Fact]
    public async Task Add_and_remove_sign_recalculate_all_pass_ratio()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantSignPermissions(f);
        await AddUser(admin, "wf-sign-ratio-starter");
        var aId = await AddUser(admin, "wf-sign-ratio-a");
        var bId = await AddUser(admin, "wf-sign-ratio-b");
        var cId = await AddUser(admin, "wf-sign-ratio-c");
        var definitionId = await Publish(admin, "加减签-比例", AllModel(aId, bId, 66));
        var starter = await ClientFor(f, "wf-sign-ratio-starter");
        var a = await ClientFor(f, "wf-sign-ratio-a");
        var b = await ClientFor(f, "wf-sign-ratio-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/add-sign",
                new { taskId, toUserId = cId, requestId = "wf-sign-ratio-add" }))
            .GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
                .GetProperty("data").GetProperty("instanceStatus").GetInt32());

        // 加签后分母为 3, 66% 的门槛为 2; b 仍可减掉 c,再按剩余两人重新计票。
        Assert.Equal(0, (await PostEnvelope(b, "/api/v1/workflow/task/remove-sign",
                new { taskId, toUserId = cId, requestId = "wf-sign-ratio-remove" }))
            .GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            (await PostEnvelope(b, "/api/v1/workflow/task/approve", new { taskId }))
                .GetProperty("data").GetProperty("instanceStatus").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        Assert.Equal(1, await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AddSign).CountAsync());
        Assert.Equal(1, await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.RemoveSign).CountAsync());
    }

    [Fact]
    public async Task Add_sign_rejects_unavailable_target_without_changing_task()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantSignPermissions(f);
        await AddUser(admin, "wf-sign-invalid-starter");
        var aId = await AddUser(admin, "wf-sign-invalid-a");
        var definitionId = await Publish(admin, "加签-非法目标", SingleModel(aId));
        var starter = await ClientFor(f, "wf-sign-invalid-starter");
        var a = await ClientFor(f, "wf-sign-invalid-a");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var result = await PostEnvelope(a, "/api/v1/workflow/task/add-sign",
            new { taskId, toUserId = 999999999L, requestId = "wf-sign-invalid-target" });
        Assert.Equal(WorkflowErrorCode.SignTargetInvalid, result.GetProperty("code").GetInt32());

        var todo = await GetEnvelope(a, "/api/v1/workflow/task/todo?Current=1&Size=20");
        Assert.Contains(todo.GetProperty("data").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("taskId").GetInt64() == taskId);
    }

    [Fact]
    public async Task Super_admin_can_add_sign_across_org_scope()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantSignPermissions(f);
        var targetId = await AddUser(admin, "wf-sign-sa-target");
        var definitionId = await Publish(admin, "加签-超管跨机构", SingleModel(1));
        var start = await PostEnvelope(admin, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var result = await PostEnvelope(admin, "/api/v1/workflow/task/add-sign",
            new { taskId, toUserId = targetId, requestId = "wf-sign-sa-cross-org" });
        Assert.Equal(0, result.GetProperty("code").GetInt32());
    }

    private static object SingleModel(long userId) => Model("any", userId);

    private static object AllModel(long a, long b, int? ratio = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new
            {
                provider = "user",
                @params = new Dictionary<string, object> { ["userIds"] = new[] { a, b } },
            },
            ["mode"] = "all",
        };
        if (ratio is not null) props["allPassRatio"] = ratio;

        return new
        {
            version = 1,
            root = new
            {
                id = "start", type = "start", name = "发起",
                next = new
                {
                    id = "approval", type = "approval", name = "审批", props, next = (object?)null,
                },
            },
        };
    }

    private static object Model(string mode, long userId) => new
    {
        version = 1,
        root = new
        {
            id = "start", type = "start", name = "发起",
            next = new
            {
                id = "approval", type = "approval", name = "审批",
                props = new
                {
                    assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } } },
                    mode,
                },
                next = (object?)null,
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

    private static async Task GrantSignPermissions(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var menuIds = await db.Queryable<SysMenu>()
            .Where(m => m.Permission == "POST:/api/v1/workflow/task/add-sign"
                        || m.Permission == "POST:/api/v1/workflow/task/remove-sign")
            .Select(m => m.Id)
            .ToListAsync();
        foreach (var menuId in menuIds)
            await db.Insertable(new SysRoleMenu { RoleId = 2, MenuId = menuId }).ExecuteCommandAsync();
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private sealed class CountingWorkflowNotifier : IWorkflowNotifier
    {
        public int TaskSignChangedCalls { get; private set; }
        public List<long> SignChangedUserIds { get; } = [];

        public Task TaskAssignedAsync(
            WfNotifyContext ctx, IReadOnlyList<long> userIds, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InstanceCompletedAsync(
            WfNotifyContext ctx, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TaskUrgedAsync(
            WfNotifyContext ctx,
            long taskId,
            long? fromUserId,
            IReadOnlyList<long> toUserIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TaskSignChangedAsync(
            WfNotifyContext ctx,
            long taskId,
            WfTaskAction action,
            IReadOnlyList<long> userIds,
            CancellationToken cancellationToken = default)
        {
            TaskSignChangedCalls++;
            SignChangedUserIds.AddRange(userIds);
            return Task.CompletedTask;
        }
    }
}
