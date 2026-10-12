using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>减签或转办移出的人不再占办理人名额,可以再次加签、转办或委托。</summary>
public class WfRemovedActorTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Removed_signer_can_be_added_delegated_and_transferred_back()
    {
        using var f = new WorkflowAppFactory();
        await GrantSignPermissions(f);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-actor-starter");
        var aId = await AddUser(admin, "wf-actor-a");
        var bId = await AddUser(admin, "wf-actor-b");
        var definitionId = await Publish(admin, "减签后再加回", SingleModel(aId));
        var starter = await ClientFor(f, "wf-actor-starter");
        var a = await ClientFor(f, "wf-actor-a");
        var b = await ClientFor(f, "wf-actor-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/add-sign", new { taskId, toUserId = bId, requestId = "wf-actor-add-1" })).GetProperty("code").GetInt32());
        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/remove-sign", new { taskId, toUserId = bId, requestId = "wf-actor-remove-1" })).GetProperty("code").GetInt32());
        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/add-sign", new { taskId, toUserId = bId, requestId = "wf-actor-add-2" })).GetProperty("code").GetInt32());
        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/remove-sign", new { taskId, toUserId = bId, requestId = "wf-actor-remove-2" })).GetProperty("code").GetInt32());

        var delegated = await PostEnvelope(a, "/api/v1/workflow/task/delegate", new { taskId, toUserId = bId });
        Assert.Equal(0, delegated.GetProperty("code").GetInt32());
        Assert.Equal(taskId, Assert.Single(await TodoItemsFor(b, instanceId)).GetProperty("taskId").GetInt64());

        var back = await PostEnvelope(b, "/api/v1/workflow/task/transfer", new { taskId, toUserId = aId });
        Assert.Equal(0, back.GetProperty("code").GetInt32());
        Assert.Equal(taskId, Assert.Single(await TodoItemsFor(a, instanceId)).GetProperty("taskId").GetInt64());
        Assert.Empty(await TodoItemsFor(b, instanceId));
    }

    [Fact]
    public async Task Transfer_back_to_the_previous_approver_succeeds()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-xfer-starter");
        var aId = await AddUser(admin, "wf-xfer-a");
        var bId = await AddUser(admin, "wf-xfer-b");
        var definitionId = await Publish(admin, "转办再转回", SingleModel(aId));
        var starter = await ClientFor(f, "wf-xfer-starter");
        var a = await ClientFor(f, "wf-xfer-a");
        var b = await ClientFor(f, "wf-xfer-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/transfer", new { taskId, toUserId = bId })).GetProperty("code").GetInt32());
        var back = await PostEnvelope(b, "/api/v1/workflow/task/transfer", new { taskId, toUserId = aId });
        Assert.Equal(0, back.GetProperty("code").GetInt32());
        Assert.NotEqual("alreadyActor", back.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object && args.TryGetProperty("reason", out var reason)
            ? reason.GetString()
            : null);
        Assert.Equal(taskId, Assert.Single(await TodoItemsFor(a, instanceId)).GetProperty("taskId").GetInt64());
    }

    [Fact]
    public async Task Pending_actor_is_still_rejected()
    {
        using var f = new WorkflowAppFactory();
        await GrantSignPermissions(f);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pending-starter");
        var aId = await AddUser(admin, "wf-pending-a");
        var bId = await AddUser(admin, "wf-pending-b");
        var definitionId = await Publish(admin, "在办人仍拒绝", AllModel(aId, bId));
        var starter = await ClientFor(f, "wf-pending-starter");
        var a = await ClientFor(f, "wf-pending-a");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var added = await PostEnvelope(a, "/api/v1/workflow/task/add-sign", new { taskId, toUserId = bId, requestId = "wf-pending-add" });
        Assert.Equal(WorkflowErrorCode.SignTargetInvalid, added.GetProperty("code").GetInt32());
        Assert.Equal("alreadyActor", added.GetProperty("args").GetProperty("reason").GetString());

        var transferred = await PostEnvelope(a, "/api/v1/workflow/task/transfer", new { taskId, toUserId = bId });
        Assert.Equal(WorkflowErrorCode.TransferTargetInvalid, transferred.GetProperty("code").GetInt32());
        Assert.Equal("alreadyActor", transferred.GetProperty("args").GetProperty("reason").GetString());

        var delegated = await PostEnvelope(a, "/api/v1/workflow/task/delegate", new { taskId, toUserId = bId });
        Assert.Equal(WorkflowErrorCode.DelegateTargetInvalid, delegated.GetProperty("code").GetInt32());
        Assert.Equal("alreadyActor", delegated.GetProperty("args").GetProperty("reason").GetString());
    }

    private static object SingleModel(long userId) => new
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
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object AllModel(long a, long b) => new
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
                    assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { a, b } } },
                    mode = "all",
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

    private static async Task<List<JsonElement>> TodoItemsFor(HttpClient client, long instanceId)
    {
        var todo = await (await client.GetAsync("/api/v1/workflow/task/todo?Current=1&Size=20")).ReadEnvelope();
        Assert.Equal(0, todo.GetProperty("code").GetInt32());
        return todo.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("instanceId").GetInt64() == instanceId)
            .ToList();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
