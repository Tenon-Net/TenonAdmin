using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2a Task 2「催办」契约测试:发起人对当前 Pending 办理人催办一次——只追加
/// <see cref="WfHistoryEventType.TaskUrged"/> 历史事件并派发 <see cref="IWorkflowNotifier.TaskUrgedAsync"/>,
/// 不进引擎、不推进 token。用 <see cref="WorkflowNotifierTests.CapturingWorkflowNotifier"/> 捕获调用点。
/// </summary>
public class WfUrgeTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Starter_urges_pending_approver_writes_history_and_notifies()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-urge-starter");
        var approverId = await AddUser(admin, "wf-urge-approver");
        var definitionId = await Publish(admin, "催办-单人", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-urge-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var urge = await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId });
        Assert.Equal(0, urge.GetProperty("code").GetInt32());

        var urgedCall = Assert.Single(capturing.TaskUrgedCalls);
        Assert.Equal(starterId, urgedCall.FromUserId);
        Assert.Equal(new[] { approverId }, urgedCall.ToUserIds);

        var history = await GetEnvelope(starter, $"/api/v1/workflow/instance/history/{instanceId}");
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        var events = history.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("eventType").GetInt32())
            .ToList();
        Assert.Contains((int)WfHistoryEventType.TaskUrged, events);
    }

    [Fact]
    public async Task Non_starter_cannot_urge()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-urge-notstarter-starter");
        var approverId = await AddUser(admin, "wf-urge-notstarter-approver");
        var definitionId = await Publish(admin, "催办-非发起人", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-urge-notstarter-starter");
        var approver = await ClientFor(f, "wf-urge-notstarter-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var urge = await PostEnvelope(approver, "/api/v1/workflow/task/urge", new { taskId });
        Assert.Equal(WorkflowErrorCode.UrgeNotAllowed, urge.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Urge_excludes_caller_from_target_list_when_caller_is_also_pending_approver()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-urge-self-starter");
        var otherApproverId = await AddUser(admin, "wf-urge-self-other");
        var definitionId = await Publish(admin, "催办-发起人为会签人",
            AnyApprovalModel(starterId, otherApproverId));

        var starter = await ClientFor(f, "wf-urge-self-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var urge = await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId });
        Assert.Equal(0, urge.GetProperty("code").GetInt32());

        var urgedCall = Assert.Single(capturing.TaskUrgedCalls);
        Assert.Equal(new[] { otherApproverId }, urgedCall.ToUserIds);
    }

    [Fact]
    public async Task Urge_on_unknown_task_returns_task_not_found()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-urge-unknown-starter");

        var starter = await ClientFor(f, "wf-urge-unknown-starter");

        var urge = await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId = 999999999L });
        Assert.Equal(WorkflowErrorCode.TaskNotFound, urge.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Urge_is_repeatable_without_rate_limit()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-urge-repeat-starter");
        var approverId = await AddUser(admin, "wf-urge-repeat-approver");
        var definitionId = await Publish(admin, "催办-可重复", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-urge-repeat-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var first = await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        var second = await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId });
        Assert.Equal(0, second.GetProperty("code").GetInt32());

        Assert.Equal(2, capturing.TaskUrgedCalls.Count);
    }

    [Fact]
    public async Task Urge_with_sole_pending_approver_being_starter_is_silent_noop()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-urge-solo-starter");
        var definitionId = await Publish(admin, "催办-发起人独任审批人", AnyApprovalModel(starterId));

        var starter = await ClientFor(f, "wf-urge-solo-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var urge = await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId });
        Assert.Equal(0, urge.GetProperty("code").GetInt32());

        Assert.Empty(capturing.TaskUrgedCalls);

        var history = await GetEnvelope(starter, $"/api/v1/workflow/instance/history/{instanceId}");
        var events = history.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("eventType").GetInt32())
            .ToList();
        Assert.DoesNotContain((int)WfHistoryEventType.TaskUrged, events);
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
        var id = await AddDefinition(admin, name, model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<long> AddDefinition(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        return added.GetProperty("data").GetInt64();
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
