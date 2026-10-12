using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 监控页管理员干预:强制转办(含空审批人阻断、没有待办)、终止、跳过阻断节点。
/// 每次都要留下管理员、动作和原因,并按现有通知通道告诉受影响的人。
/// </summary>
public class WfAdminInterventionTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Force_transfer_pending_reassigns_records_and_notifies()
    {
        var notifier = new CountingWorkflowNotifier();
        using var f = Factory(notifier);
        var admin = await ClientFor(f, "superAdmin");
        var adminId = await UserId(f, "superAdmin");
        await AddUser(admin, "wf-admin-tf-starter");
        var fromId = await AddUser(admin, "wf-admin-tf-from");
        var toId = await AddUser(admin, "wf-admin-tf-to");
        var definitionId = await Publish(admin, "干预-转办待办", UserModel(fromId));
        var starter = await ClientFor(f, "wf-admin-tf-starter");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        var moved = await PostEnvelope(admin, "/api/v1/workflow/instance/force-transfer", new
        {
            instanceId,
            toUserId = toId,
            reason = "原办理人无法处理",
        });
        Assert.Equal(0, moved.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running, moved.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var his = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AdminTransfer)
            .FirstAsync();
        Assert.Equal(adminId, his.UserId);
        Assert.Equal(toId, his.TransferToUserId);
        Assert.Equal("原办理人无法处理", his.Comment);
        Assert.Equal("审批", his.NodeName);

        var history = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.AdminForceTransferred)
            .FirstAsync();
        Assert.Equal(adminId, history.ActorUserId);
        Assert.Equal(WfHistoryActorType.Human, history.ActorType);
        using (var payload = JsonDocument.Parse(history.PayloadJson!))
        {
            Assert.Equal("原办理人无法处理", payload.RootElement.GetProperty("reason").GetString());
            Assert.Equal(toId, payload.RootElement.GetProperty("toUserId").GetInt64());
            Assert.Equal(adminId, payload.RootElement.GetProperty("adminUserId").GetInt64());
        }

        var actors = await db.Queryable<WfTaskActor>().Where(a => a.TaskId == his.TaskId).ToListAsync();
        Assert.Equal(WfActorStatus.Skipped, Assert.Single(actors, a => a.UserId == fromId).Status);
        Assert.Equal(WfActorStatus.Pending, Assert.Single(actors, a => a.UserId == toId).Status);
        Assert.Contains(toId, notifier.Assigned);
        Assert.Contains(fromId, notifier.Recalled);
    }

    [Fact]
    public async Task Force_transfer_blocked_node_creates_a_task()
    {
        var notifier = new CountingWorkflowNotifier();
        using var f = Factory(notifier);
        var admin = await ClientFor(f, "superAdmin");
        var adminId = await UserId(f, "superAdmin");
        await AddUser(admin, "wf-admin-block-starter");
        var toId = await AddUser(admin, "wf-admin-block-to");
        var definitionId = await Publish(admin, "干预-阻断转办", LeaderModel("部门主管"));
        var starter = await ClientFor(f, "wf-admin-block-starter");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(JsonValueKind.Null, started.GetProperty("data").GetProperty("createdTaskId").ValueKind);
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        var moved = await PostEnvelope(admin, "/api/v1/workflow/instance/force-transfer", new
        {
            instanceId,
            toUserId = toId,
            reason = "阻断后指定办理人",
        });
        Assert.Equal(0, moved.GetProperty("code").GetInt32());
        Assert.True(moved.GetProperty("data").GetProperty("createdTaskId").GetInt64() > 0);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(1, await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).CountAsync());
        var his = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AdminTransfer)
            .FirstAsync();
        Assert.Equal(adminId, his.UserId);
        Assert.Equal(toId, his.TransferToUserId);
        Assert.Equal("阻断后指定办理人", his.Comment);
        Assert.Equal("部门主管", his.NodeName);
        var actor = await db.Queryable<WfTaskActor>().Where(a => a.TaskId == his.TaskId).FirstAsync();
        Assert.Equal(toId, actor.UserId);
        Assert.Equal(WfActorStatus.Pending, actor.Status);
        Assert.Contains(toId, notifier.Assigned);
        Assert.Empty(notifier.Recalled);
    }

    [Fact]
    public async Task Force_transfer_rejects_blank_reason_disabled_user_and_current_actor()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-admin-bad-starter");
        var fromId = await AddUser(admin, "wf-admin-bad-from");
        var disabledId = await AddUser(admin, "wf-admin-bad-disabled");
        Assert.Equal(0, (await PutEnvelope(admin, $"/api/v1/sys/user/{disabledId}/enabled", new { enabled = false }))
            .GetProperty("code").GetInt32());
        var definitionId = await Publish(admin, "干预-拒绝转办", UserModel(fromId));
        var starter = await ClientFor(f, "wf-admin-bad-starter");
        var instanceId = (await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId }))
            .GetProperty("data").GetProperty("instanceId").GetInt64();

        var blank = await PostEnvelope(admin, "/api/v1/workflow/instance/force-transfer", new
        {
            instanceId,
            toUserId = disabledId,
            reason = "  ",
        });
        Assert.Equal(WorkflowErrorCode.AdminInterventionNotAllowed, blank.GetProperty("code").GetInt32());
        Assert.Equal("reasonRequired", blank.GetProperty("args").GetProperty("reason").GetString());

        var disabled = await PostEnvelope(admin, "/api/v1/workflow/instance/force-transfer", new
        {
            instanceId,
            toUserId = disabledId,
            reason = "人已停用",
        });
        Assert.Equal(WorkflowErrorCode.AdminInterventionNotAllowed, disabled.GetProperty("code").GetInt32());
        Assert.Equal("userUnavailable", disabled.GetProperty("args").GetProperty("reason").GetString());

        var same = await PostEnvelope(admin, "/api/v1/workflow/instance/force-transfer", new
        {
            instanceId,
            toUserId = fromId,
            reason = "已经是办理人",
        });
        Assert.Equal(WorkflowErrorCode.AdminInterventionNotAllowed, same.GetProperty("code").GetInt32());
        Assert.Equal("alreadyActor", same.GetProperty("args").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Terminate_requires_reason_then_stops_and_notifies()
    {
        var notifier = new CountingWorkflowNotifier();
        using var f = Factory(notifier);
        var admin = await ClientFor(f, "superAdmin");
        var adminId = await UserId(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-admin-stop-starter");
        var approverId = await AddUser(admin, "wf-admin-stop-approver");
        var definitionId = await Publish(admin, "干预-终止", UserModel(approverId));
        var starter = await ClientFor(f, "wf-admin-stop-starter");
        var instanceId = (await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId }))
            .GetProperty("data").GetProperty("instanceId").GetInt64();

        var blank = await PostEnvelope(admin, "/api/v1/workflow/instance/terminate", new { instanceId, reason = "" });
        Assert.Equal(WorkflowErrorCode.AdminInterventionNotAllowed, blank.GetProperty("code").GetInt32());
        Assert.Equal("reasonRequired", blank.GetProperty("args").GetProperty("reason").GetString());

        var stopped = await PostEnvelope(admin, "/api/v1/workflow/instance/terminate", new
        {
            instanceId,
            reason = "业务取消",
            requestId = "wf-admin-stop-1",
        });
        Assert.Equal(0, stopped.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Terminated, stopped.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var replay = await PostEnvelope(admin, "/api/v1/workflow/instance/terminate", new
        {
            instanceId,
            reason = "业务取消",
            requestId = "wf-admin-stop-1",
        });
        Assert.Equal(0, replay.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Terminated, replay.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var his = Assert.Single(await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AdminTerminate)
            .ToListAsync());
        Assert.Equal(adminId, his.UserId);
        Assert.Equal("业务取消", his.Comment);
        var completed = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.InstanceCompleted)
            .FirstAsync();
        Assert.Equal(adminId, completed.ActorUserId);
        using (var payload = JsonDocument.Parse(completed.PayloadJson!))
        {
            Assert.Equal("Terminated", payload.RootElement.GetProperty("status").GetString());
            Assert.Equal("业务取消", payload.RootElement.GetProperty("reason").GetString());
            Assert.Equal(adminId, payload.RootElement.GetProperty("adminUserId").GetInt64());
        }
        Assert.Equal(0, await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).CountAsync());
        Assert.Contains((starterId, WfInstanceStatus.Terminated), notifier.Completed);
        Assert.Contains(approverId, notifier.Recalled);
    }

    [Fact]
    public async Task Skip_blocked_node_advances_and_records_why()
    {
        var notifier = new CountingWorkflowNotifier();
        using var f = Factory(notifier);
        var admin = await ClientFor(f, "superAdmin");
        var adminId = await UserId(f, "superAdmin");
        await AddUser(admin, "wf-admin-skip-starter");
        var nextId = await AddUser(admin, "wf-admin-skip-next");
        var definitionId = await Publish(admin, "干预-跳过", BlockedThenUser(nextId));
        var starter = await ClientFor(f, "wf-admin-skip-starter");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(JsonValueKind.Null, started.GetProperty("data").GetProperty("createdTaskId").ValueKind);
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        var skipped = await PostEnvelope(admin, "/api/v1/workflow/instance/skip", new
        {
            instanceId,
            reason = "主管缺失,先往后走",
        });
        Assert.Equal(0, skipped.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running, skipped.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.True(skipped.GetProperty("data").GetProperty("createdTaskId").GetInt64() > 0);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var his = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AdminSkip)
            .FirstAsync();
        Assert.Equal(adminId, his.UserId);
        Assert.Equal("主管缺失,先往后走", his.Comment);
        Assert.Equal("部门主管", his.NodeName);
        Assert.Null(his.TaskId);
        var history = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.AdminSkipped)
            .FirstAsync();
        Assert.Equal(adminId, history.ActorUserId);
        using (var payload = JsonDocument.Parse(history.PayloadJson!))
        {
            Assert.Equal("主管缺失,先往后走", payload.RootElement.GetProperty("reason").GetString());
            Assert.Equal(adminId, payload.RootElement.GetProperty("adminUserId").GetInt64());
            Assert.Equal("leader", payload.RootElement.GetProperty("nodeId").GetString());
        }
        var task = await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).FirstAsync();
        Assert.Equal("approve", task.NodeId);
        var actor = await db.Queryable<WfTaskActor>().Where(a => a.TaskId == task.Id).FirstAsync();
        Assert.Equal(nextId, actor.UserId);
        Assert.Contains(nextId, notifier.Assigned);
    }

    [Fact]
    public async Task Skip_rejects_a_node_that_still_has_a_task()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-admin-skip-deny-starter");
        var approverId = await AddUser(admin, "wf-admin-skip-deny-user");
        var definitionId = await Publish(admin, "干预-不能跳过待办", UserModel(approverId));
        var starter = await ClientFor(f, "wf-admin-skip-deny-starter");
        var instanceId = (await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId }))
            .GetProperty("data").GetProperty("instanceId").GetInt64();

        var denied = await PostEnvelope(admin, "/api/v1/workflow/instance/skip", new
        {
            instanceId,
            reason = "想跳过正在审的人",
        });
        Assert.Equal(WorkflowErrorCode.AdminInterventionNotAllowed, denied.GetProperty("code").GetInt32());
        Assert.Equal("taskStillOpen", denied.GetProperty("args").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Missing_permission_is_forbidden_and_granted_role_can_terminate()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-admin-perm-denied");
        var starterId = await AddUser(admin, "wf-admin-perm-starter");
        var definitionId = await Publish(admin, "干预-权限", LeaderModel("空节点"));
        var starter = await ClientFor(f, "wf-admin-perm-starter");
        var instanceId = (await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId }))
            .GetProperty("data").GetProperty("instanceId").GetInt64();

        var denied = await ClientFor(f, "wf-admin-perm-denied");
        var forbidden = await denied.PostJson("/api/v1/workflow/instance/terminate", new
        {
            instanceId,
            reason = "没有按钮权限",
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await Grant(f, "POST:/api/v1/workflow/instance/terminate");
        using (var cacheScope = f.Services.CreateScope())
        {
            var cache = cacheScope.ServiceProvider.GetRequiredService<ICacheProvider>();
            await cache.RemoveAsync(CacheKeys.UserPermissions(starterId));
        }
        var allowedClient = await ClientFor(f, "wf-admin-perm-starter");
        var allowed = await PostEnvelope(allowedClient, "/api/v1/workflow/instance/terminate", new
        {
            instanceId,
            reason = "授权后终止",
        });
        Assert.Equal(0, allowed.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Terminated, allowed.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var his = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.AdminTerminate)
            .FirstAsync();
        Assert.Equal(starterId, his.UserId);
        Assert.Equal("授权后终止", his.Comment);
    }

    private static WorkflowAppFactory Factory(CountingWorkflowNotifier notifier) => new()
    {
        Overrides = services => services.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(notifier)),
    };

    private static object UserModel(long userId) => Chain(new
    {
        id = "approve",
        type = "approval",
        name = "审批",
        props = new
        {
            assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } } },
            mode = "any",
            nobody = "block",
        },
        next = (object?)null,
    });

    private static object LeaderModel(string name) => Chain(new
    {
        id = "leader",
        type = "approval",
        name,
        props = new
        {
            assignee = new { provider = "leader", @params = new { level = 1 } },
            mode = "any",
            nobody = "block",
        },
        next = (object?)null,
    });

    private static object BlockedThenUser(long userId) => Chain(new
    {
        id = "leader",
        type = "approval",
        name = "部门主管",
        props = new
        {
            assignee = new { provider = "leader", @params = new { level = 1 } },
            mode = "any",
            nobody = "block",
        },
        next = new
        {
            id = "approve",
            type = "approval",
            name = "复核",
            props = new
            {
                assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } } },
                mode = "any",
                nobody = "block",
            },
            next = (object?)null,
        },
    });

    private static object Chain(object first) => new
    {
        version = 1,
        root = new { id = "start", type = "start", name = "", next = first },
    };

    private static async Task Grant(WorkflowAppFactory f, string permission)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var menuId = await db.Queryable<SysMenu>()
            .Where(m => m.Permission == permission)
            .Select(m => m.Id)
            .FirstAsync();
        await db.Insertable(new SysRoleMenu { RoleId = 2, MenuId = menuId }).ExecuteCommandAsync();
    }

    private static async Task<long> UserId(WorkflowAppFactory f, string account)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<SysUser>().Where(u => u.Account == account).Select(u => u.Id).FirstAsync();
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account, long? directorId = null)
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
        if (directorId is not null) body["directorId"] = directorId;
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

    private static async Task<JsonElement> PutEnvelope(HttpClient client, string path, object body) =>
        await (await client.PutJson(path, body)).ReadEnvelope();

    private sealed class CountingWorkflowNotifier : IWorkflowNotifier
    {
        public List<long> Assigned { get; } = [];
        public List<long> Recalled { get; } = [];
        public List<(long StarterUserId, WfInstanceStatus Status)> Completed { get; } = [];

        public Task TaskAssignedAsync(
            WfNotifyContext ctx, IReadOnlyList<long> userIds, CancellationToken cancellationToken = default)
        {
            Assigned.AddRange(userIds);
            return Task.CompletedTask;
        }

        public Task InstanceCompletedAsync(WfNotifyContext ctx, CancellationToken cancellationToken = default)
        {
            Completed.Add((ctx.StarterUserId, ctx.Status));
            return Task.CompletedTask;
        }

        public Task TaskUrgedAsync(
            WfNotifyContext ctx, long taskId, long? fromUserId, IReadOnlyList<long> toUserIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TaskRecalledAsync(
            WfNotifyContext ctx, long taskId, IReadOnlyList<long> userIds,
            CancellationToken cancellationToken = default)
        {
            Recalled.AddRange(userIds);
            return Task.CompletedTask;
        }
    }
}
