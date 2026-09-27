using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>比例票签的发布边界、计票、提前收敛与 CAS 契约。</summary>
public class WfAllPassRatioTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Default_ratio_keeps_full_approval_behavior()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ratio-default-starter");
        var users = await AddApprovers(admin, "default", 3);
        var definitionId = await Publish(admin, "比例-默认全票", ApprovalModel(users, "all"));
        var starter = await ClientFor(f, "wf-ratio-default-starter");
        var clients = await ClientsFor(f, "default", 3);
        var (instanceId, taskId) = await Start(starter, definitionId);

        Assert.Equal(0, (await Approve(clients[0], taskId)).GetProperty("code").GetInt32());
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(0, (await Approve(clients[1], taskId)).GetProperty("code").GetInt32());
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(0, (await Approve(clients[2], taskId)).GetProperty("code").GetInt32());

        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, instanceId));
        Assert.Equal(3, await HistoryCount(f, instanceId, WfTaskAction.Approve));
    }

    [Fact]
    public async Task Low_ratio_passes_early_and_late_action_conflicts_without_extra_history()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ratio-pass-starter");
        var users = await AddApprovers(admin, "pass", 3);
        var definitionId = await Publish(admin, "比例-提前通过", ApprovalModel(users, "all", 66));
        var starter = await ClientFor(f, "wf-ratio-pass-starter");
        var clients = await ClientsFor(f, "pass", 3);
        var (instanceId, taskId) = await Start(starter, definitionId);

        Assert.Equal(0, (await Approve(clients[0], taskId)).GetProperty("code").GetInt32());
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(0, (await Approve(clients[1], taskId)).GetProperty("code").GetInt32());
        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, instanceId));

        var late = await Approve(clients[2], taskId);
        Assert.Equal(WorkflowErrorCode.TaskConflict, late.GetProperty("code").GetInt32());
        Assert.Equal(2, await HistoryCount(f, instanceId, WfTaskAction.Approve));
        Assert.Equal(WfActorStatus.Skipped, await ActorStatus(f, taskId, users[2]));
    }

    [Fact]
    public async Task Reject_waits_while_threshold_is_reachable_then_fails_early()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ratio-fail-starter");
        var users = await AddApprovers(admin, "fail", 4);
        var definitionId = await Publish(admin, "比例-提前失败", ApprovalModel(users, "all", 75));
        var starter = await ClientFor(f, "wf-ratio-fail-starter");
        var clients = await ClientsFor(f, "fail", 4);
        var (instanceId, taskId) = await Start(starter, definitionId);

        Assert.Equal(0, (await Approve(clients[0], taskId)).GetProperty("code").GetInt32());
        Assert.Equal(0, (await Reject(clients[1], taskId)).GetProperty("code").GetInt32());
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(0, (await Reject(clients[2], taskId)).GetProperty("code").GetInt32());

        Assert.Equal(WfInstanceStatus.Rejected, await InstanceStatus(f, instanceId));
        Assert.Equal(1, await HistoryCount(f, instanceId, WfTaskAction.Approve));
        Assert.Equal(2, await HistoryCount(f, instanceId, WfTaskAction.Reject));
        Assert.Equal(WfActorStatus.Skipped, await ActorStatus(f, taskId, users[3]));
    }

    [Theory]
    [InlineData("all", 0, "allPassRatioOutOfRange")]
    [InlineData("all", 101, "allPassRatioOutOfRange")]
    [InlineData("seq", 99, "sequentialAllPassRatioInvalid")]
    public async Task Publish_rejects_invalid_ratio_boundaries(string mode, int ratio, string reason)
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var userId = await AddUser(admin, $"wf-ratio-invalid-{mode}-{ratio}");
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add",
            new { name = $"非法比例-{mode}-{ratio}", model = ApprovalModel([userId], mode, ratio) });
        Assert.Equal(0, added.GetProperty("code").GetInt32());

        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish",
            new { id = added.GetProperty("data").GetInt64() });

        Assert.Equal(WorkflowErrorCode.ModelInvalid, published.GetProperty("code").GetInt32());
        Assert.Equal(reason, published.GetProperty("args").GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("all", 1)]
    [InlineData("all", 100)]
    [InlineData("any", 0)]
    [InlineData("seq", 100)]
    public async Task Publish_accepts_closed_interval_and_ignored_any_ratio(string mode, int ratio)
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var userId = await AddUser(admin, $"wf-ratio-valid-{mode}-{ratio}");

        var definitionId = await Publish(admin, $"合法比例-{mode}-{ratio}", ApprovalModel([userId], mode, ratio));

        Assert.True(definitionId > 0);
    }

    private static object ApprovalModel(long[] userIds, string mode, int? ratio = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new
            {
                provider = "user",
                @params = new Dictionary<string, object> { ["userIds"] = userIds },
            },
            ["mode"] = mode,
        };
        if (ratio is not null) props["allPassRatio"] = ratio;
        return new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = new { id = "approval", type = "approval", name = "审批", props, next = (object?)null },
            },
        };
    }

    private static async Task<long[]> AddApprovers(HttpClient admin, string prefix, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++) ids[i] = await AddUser(admin, $"wf-ratio-{prefix}-{i}");
        return ids;
    }

    private static async Task<HttpClient[]> ClientsFor(WorkflowAppFactory f, string prefix, int count)
    {
        var clients = new HttpClient[count];
        for (var i = 0; i < count; i++) clients[i] = await ClientFor(f, $"wf-ratio-{prefix}-{i}");
        return clients;
    }

    private static async Task<(long InstanceId, long TaskId)> Start(HttpClient starter, long definitionId)
    {
        var result = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, result.GetProperty("code").GetInt32());
        return (result.GetProperty("data").GetProperty("instanceId").GetInt64(),
            result.GetProperty("data").GetProperty("createdTaskId").GetInt64());
    }

    private static Task<JsonElement> Approve(HttpClient client, long taskId) =>
        PostEnvelope(client, "/api/v1/workflow/task/approve", new { taskId });

    private static Task<JsonElement> Reject(HttpClient client, long taskId) =>
        PostEnvelope(client, "/api/v1/workflow/task/reject", new { taskId });

    private static async Task<WfInstanceStatus> InstanceStatus(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfInstance>().Where(i => i.Id == instanceId).Select(i => i.Status).FirstAsync();
    }

    private static async Task<WfActorStatus> ActorStatus(WorkflowAppFactory f, long taskId, long userId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfTaskActor>().Where(a => a.TaskId == taskId && a.UserId == userId)
            .Select(a => a.Status).FirstAsync();
    }

    private static async Task<int> HistoryCount(WorkflowAppFactory f, long instanceId, WfTaskAction action)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfHisTask>().Where(h => h.InstanceId == instanceId && h.Action == action).CountAsync();
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
        var result = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account,
            password = Password,
            name = account,
            enabled = true,
            orgId = 1,
            roleIds = new[] { 2L },
        });
        Assert.Equal(0, result.GetProperty("code").GetInt32());
        return result.GetProperty("data").GetProperty("id").GetInt64();
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
