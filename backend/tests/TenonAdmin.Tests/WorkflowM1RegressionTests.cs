using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

public class WorkflowM1RegressionTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Initiator_scope_snapshot_and_instance_acl_are_enforced()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-scope-starter");
        var approverId = await AddUser(admin, "wf-scope-approver");
        await AddUser(admin, "wf-scope-outsider");

        var publishedModel = UserApprovalModel(
            approverId,
            [new { type = "user", id = starterId }],
            "views/published/form");
        var definitionId = await Publish(admin, "范围流程", publishedModel);
        await UpdateDefinition(admin, definitionId, "范围流程",
            UserApprovalModel(approverId, [new { type = "user", id = starterId }], "views/draft/form"));

        var starter = await ClientFor(f, "wf-scope-starter");
        var outsider = await ClientFor(f, "wf-scope-outsider");
        var approver = await ClientFor(f, "wf-scope-approver");

        var list = await GetEnvelope(starter, "/api/v1/workflow/instance/startable");
        Assert.Contains(list.GetProperty("data").EnumerateArray(),
            d => d.GetProperty("id").GetInt64() == definitionId);
        var outsiderList = await GetEnvelope(outsider, "/api/v1/workflow/instance/startable");
        Assert.DoesNotContain(outsiderList.GetProperty("data").EnumerateArray(),
            d => d.GetProperty("id").GetInt64() == definitionId);

        var snapshot = await GetEnvelope(starter, $"/api/v1/workflow/instance/startable/{definitionId}");
        Assert.Equal("views/published/form",
            snapshot.GetProperty("data").GetProperty("model").GetProperty("formComponent").GetString());
        Assert.Equal(WorkflowErrorCode.InitiatorNotAllowed,
            (await GetEnvelope(outsider, $"/api/v1/workflow/instance/startable/{definitionId}"))
            .GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.InitiatorNotAllowed,
            (await PostEnvelope(outsider, "/api/v1/workflow/instance/start", new { definitionId }))
            .GetProperty("code").GetInt32());

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId, businessKey = "ACL-1" });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal("views/published/form", detail.GetProperty("data").GetProperty("formComponent").GetString());
        Assert.Equal(0,
            (await GetEnvelope(approver, $"/api/v1/workflow/instance/{instanceId}"))
            .GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.InstanceAccessDenied,
            (await GetEnvelope(outsider, $"/api/v1/workflow/instance/{instanceId}"))
            .GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.InstanceAccessDenied,
            (await GetEnvelope(outsider, $"/api/v1/workflow/instance/history/{instanceId}"))
            .GetProperty("code").GetInt32());

        Assert.Equal(0,
            (await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());
        Assert.Equal(0,
            (await GetEnvelope(approver, $"/api/v1/workflow/instance/{instanceId}"))
            .GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Multi_level_leaders_are_promoted_in_order()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var secondId = await AddUser(admin, "wf-seq-second");
        var firstId = await AddUser(admin, "wf-seq-first", directorId: secondId);
        await AddUser(admin, "wf-seq-starter", directorId: firstId);
        var definitionId = await Publish(admin, "顺序主管", MultiLeaderModel());

        var starter = await ClientFor(f, "wf-seq-starter");
        var first = await ClientFor(f, "wf-seq-first");
        var second = await ClientFor(f, "wf-seq-second");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(1, TodoCount(await GetEnvelope(first, "/api/v1/workflow/task/todo?Current=1&Size=20")));
        Assert.Equal(0, TodoCount(await GetEnvelope(second, "/api/v1/workflow/task/todo?Current=1&Size=20")));
        Assert.Equal(WorkflowErrorCode.TaskConflict,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        var firstApprove = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, firstApprove.GetProperty("code").GetInt32());
        Assert.Contains(secondId,
            firstApprove.GetProperty("data").GetProperty("newAssigneeUserIds")
                .EnumerateArray().Select(x => x.GetInt64()));
        Assert.Equal(1, TodoCount(await GetEnvelope(second, "/api/v1/workflow/task/todo?Current=1&Size=20")));
        Assert.Equal((int)WfInstanceStatus.Approved,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Any_sign_has_one_winner_and_self_select_survives_context_restore()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-any-starter");
        var firstId = await AddUser(admin, "wf-any-first");
        var secondId = await AddUser(admin, "wf-any-second");
        var anyDefinitionId = await Publish(admin, "或签", AnyModel(firstId, secondId));

        var starter = await ClientFor(f, "wf-any-starter");
        var first = await ClientFor(f, "wf-any-first");
        var second = await ClientFor(f, "wf-any-second");
        var anyStart = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId = anyDefinitionId });
        var anyTaskId = anyStart.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal(0,
            (await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId = anyTaskId }))
            .GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.TaskConflict,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId = anyTaskId }))
            .GetProperty("code").GetInt32());

        var selfDefinitionId = await Publish(admin, "跨节点自选", SelfSelectModel());
        var selfStart = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new
        {
            definitionId = selfDefinitionId,
            selectedUserIdsByNode = new Dictionary<string, long[]> { ["pick-2"] = [secondId] },
        });
        var firstTaskId = selfStart.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var firstAdvance = await PostEnvelope(starter, "/api/v1/workflow/task/approve",
            new { taskId = firstTaskId });
        Assert.Contains(secondId,
            firstAdvance.GetProperty("data").GetProperty("newAssigneeUserIds")
                .EnumerateArray().Select(x => x.GetInt64()));
        Assert.Equal(1, TodoCount(await GetEnvelope(second, "/api/v1/workflow/task/todo?Current=1&Size=20")));
    }

    [Fact]
    public async Task Transfer_and_publish_boundaries_return_workflow_errors()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-transfer-starter");
        var approverId = await AddUser(admin, "wf-transfer-approver");
        var disabledId = await AddUser(admin, "wf-transfer-disabled", enabled: false);
        var definitionId = await Publish(admin, "转办", UserApprovalModel(approverId));
        var starter = await ClientFor(f, "wf-transfer-starter");
        var approver = await ClientFor(f, "wf-transfer-approver");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(WorkflowErrorCode.TransferTargetInvalid,
            (await PostEnvelope(approver, "/api/v1/workflow/task/transfer",
                new { taskId, toUserId = 9_999_999L }))
            .GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.TransferTargetInvalid,
            (await PostEnvelope(approver, "/api/v1/workflow/task/transfer",
                new { taskId, toUserId = disabledId }))
            .GetProperty("code").GetInt32());

        var unknownId = await AddDefinition(admin, "未知 Provider", UserApprovalModel(0, provider: "missing"));
        Assert.Equal(WorkflowErrorCode.ProviderNotRegistered,
            (await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = unknownId }))
            .GetProperty("code").GetInt32());
        var longNameId = await AddDefinition(admin, "过长节点", LongNodeNameModel());
        Assert.Equal(WorkflowErrorCode.ModelFieldTooLong,
            (await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = longNameId }))
            .GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Definition_delete_allows_draft_and_blocks_running()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var approverId = await AddUser(admin, "wf-del-approver");
        var starterId = await AddUser(admin, "wf-del-starter");
        var model = UserApprovalModel(approverId, [new { type = "user", id = starterId }]);

        var draftId = await AddDefinition(admin, "可删草稿", model);
        Assert.Equal(0,
            (await (await admin.DeleteAsync($"/api/v1/workflow/definition/{draftId}")).ReadEnvelope())
            .GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.DefinitionNotFound,
            (await GetEnvelope(admin, $"/api/v1/workflow/definition/{draftId}"))
            .GetProperty("code").GetInt32());

        var publishedId = await Publish(admin, "有在途不能删", model);
        var starter = await ClientFor(f, "wf-del-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId = publishedId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        Assert.Equal(WorkflowErrorCode.DefinitionHasRunningInstances,
            (await (await admin.DeleteAsync($"/api/v1/workflow/definition/{publishedId}")).ReadEnvelope())
            .GetProperty("code").GetInt32());

        var approver = await ClientFor(f, "wf-del-approver");
        Assert.Equal(0,
            (await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());
        Assert.Equal(0,
            (await (await admin.DeleteAsync($"/api/v1/workflow/definition/{publishedId}")).ReadEnvelope())
            .GetProperty("code").GetInt32());
        Assert.Equal(0,
            (await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}"))
            .GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Shared_test_host_does_not_enable_workflow()
    {
        using var f = new AdminAppFactory();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/workflow/instance/startable")).StatusCode);
    }

    private static int TodoCount(JsonElement envelope) =>
        envelope.GetProperty("data").GetProperty("items").GetArrayLength();

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(
        HttpClient admin,
        string account,
        bool enabled = true,
        long? directorId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = enabled,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
            ["directorId"] = directorId,
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

    private static async Task UpdateDefinition(HttpClient admin, long id, string name, object model)
    {
        var updated = await PostEnvelope(admin, "/api/v1/workflow/definition/update",
            new { id, name, model });
        Assert.Equal(0, updated.GetProperty("code").GetInt32());
    }

    private static object UserApprovalModel(
        long userId,
        object[]? scope = null,
        string? formComponent = null,
        string provider = "user") => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            props = new { initiatorScope = scope ?? [] },
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider,
                        @params = provider == "user"
                            ? new Dictionary<string, object> { ["userIds"] = new[] { userId } }
                            : new Dictionary<string, object>(),
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
        formComponent,
    };

    private static object MultiLeaderModel() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "leaders",
                type = "approval",
                name = "逐级审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "multiLeader",
                        @params = new Dictionary<string, object> { ["level"] = 2 },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object AnyModel(long firstId, long secondId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "any",
                type = "approval",
                name = "或签",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { firstId, secondId } },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object SelfSelectModel() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "initiator-1",
                type = "approval",
                name = "发起人确认",
                props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                next = new
                {
                    id = "pick-2",
                    type = "approval",
                    name = "自选审批",
                    props = new { assignee = new { provider = "selfSelect", @params = new { } }, mode = "any" },
                    next = (object?)null,
                },
            },
        },
    };

    private static object LongNodeNameModel() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "long",
                type = "approval",
                name = new string('x', 129),
                props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                next = (object?)null,
            },
        },
    };

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
