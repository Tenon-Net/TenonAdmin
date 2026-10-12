using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 运行时解析不到审批人:默认阻断并记历史;只有显式 autoPass 才自动通过;转交同样留记录。
/// </summary>
public class WfNobodyPolicyTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Missing_manager_blocks_and_records_the_node()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nobody-block-starter");
        var definitionId = await Publish(admin, "空审批人-阻断", LeaderModel("部门主管"));
        var starter = await ClientFor(f, "wf-nobody-block-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running, start.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, start.GetProperty("data").GetProperty("createdTaskId").ValueKind);

        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var (history, tasks) = await NobodyRows(f, instanceId);
        var row = Assert.Single(history);
        Assert.Equal("leader", row.NodeId);
        Assert.Contains("\"action\":\"block\"", row.PayloadJson);
        Assert.Contains("\"reason\":\"noResolvedUsers\"", row.PayloadJson);
        Assert.Contains("部门主管", row.PayloadJson);

        var his = Assert.Single(tasks);
        Assert.Equal(WfTaskAction.Nobody, his.Action);
        Assert.Equal("部门主管", his.NodeName);
        Assert.Equal("block|noResolvedUsers", his.Comment);
        Assert.Equal(0, his.UserId);
        Assert.Equal(0, await TaskCount(f, instanceId));
    }

    [Fact]
    public async Task Disabled_manager_blocks_the_same_way()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var managerId = await AddUser(admin, "wf-nobody-manager");
        await AddUser(admin, "wf-nobody-disabled-starter", managerId);
        var disabled = await PutEnvelope(admin, $"/api/v1/sys/user/{managerId}/enabled", new { enabled = false });
        Assert.Equal(0, disabled.GetProperty("code").GetInt32());

        var definitionId = await Publish(admin, "空审批人-主管停用", LeaderModel("直属主管"));
        var starter = await ClientFor(f, "wf-nobody-disabled-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running, start.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var (_, tasks) = await NobodyRows(f, instanceId);
        Assert.Equal("block|noResolvedUsers", Assert.Single(tasks).Comment);
    }

    [Fact]
    public async Task Explicit_auto_pass_completes_and_records_why()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nobody-pass-starter");
        var definitionId = await Publish(admin, "空审批人-自动通过", LeaderModel("自动节点", "autoPass"));
        var starter = await ClientFor(f, "wf-nobody-pass-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved, start.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var (history, tasks) = await NobodyRows(f, instanceId);
        Assert.Contains("\"action\":\"autoPass\"", Assert.Single(history).PayloadJson);
        Assert.Equal("autoPass|noResolvedUsers", Assert.Single(tasks).Comment);
        Assert.Equal("自动节点", tasks[0].NodeName);
    }

    [Fact]
    public async Task Explicit_transfer_assigns_the_fallback_and_records_why()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nobody-transfer-starter");
        var fallbackId = await AddUser(admin, "wf-nobody-fallback");
        var definitionId = await Publish(admin, "空审批人-转交", LeaderModel("转交节点", "transfer", fallbackId));
        var starter = await ClientFor(f, "wf-nobody-transfer-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running, start.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.True(start.GetProperty("data").GetProperty("createdTaskId").GetInt64() > 0);

        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var (history, tasks) = await NobodyRows(f, instanceId);
        Assert.Contains($"\"toUserId\":{fallbackId}", Assert.Single(history).PayloadJson);
        var his = Assert.Single(tasks);
        Assert.Equal("transfer|noResolvedUsers", his.Comment);
        Assert.Equal(fallbackId, his.TransferToUserId);
        Assert.Equal("转交节点", his.NodeName);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var actor = await db.Queryable<WfTaskActor>().Where(a => a.UserId == fallbackId && a.Status == WfActorStatus.Pending).FirstAsync();
        Assert.NotNull(actor);
    }

    [Fact]
    public async Task Block_keeps_the_previous_approval()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var approverId = await AddUser(admin, "wf-nobody-keep-a");
        await AddUser(admin, "wf-nobody-keep-starter");
        var definitionId = await Publish(admin, "空审批人-保留前序", TwoStepModel(approverId));
        var starter = await ClientFor(f, "wf-nobody-keep-starter");
        var approver = await ClientFor(f, "wf-nobody-keep-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approved = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running, approved.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var approve = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve)
            .FirstAsync();
        Assert.Equal(approverId, approve.UserId);
        var nobody = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Nobody)
            .FirstAsync();
        Assert.Equal("leader", nobody.NodeId);
        Assert.Equal("block|noResolvedUsers", nobody.Comment);
    }

    private static object LeaderModel(string name, string? nobody = null, long? transferUserId = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new { provider = "leader", @params = new { level = 1 } },
            ["mode"] = "any",
        };
        if (nobody is not null) props["nobody"] = nobody;
        if (transferUserId is not null) props["nobodyTransferUserId"] = transferUserId;
        return Chain(new
        {
            id = "leader",
            type = "approval",
            name,
            props,
            next = (object?)null,
        });
    }

    private static object TwoStepModel(long approverId) => Chain(new
    {
        id = "first",
        type = "approval",
        name = "初审",
        props = new
        {
            assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { approverId } } },
            mode = "any",
        },
        next = new
        {
            id = "leader",
            type = "approval",
            name = "主管",
            props = new
            {
                assignee = new { provider = "leader", @params = new { level = 1 } },
                mode = "any",
            },
            next = (object?)null,
        },
    });

    private static object Chain(object first) => new
    {
        version = 1,
        root = new { id = "start", type = "start", name = "", next = first },
    };

    private static async Task<(List<WfHistory> History, List<WfHisTask> Tasks)> NobodyRows(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var history = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.NobodyHandled)
            .ToListAsync();
        var tasks = await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Nobody)
            .ToListAsync();
        return (history, tasks);
    }

    private static async Task<int> TaskCount(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).CountAsync();
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
        if (directorId is long id) body["directorId"] = id;
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
}
