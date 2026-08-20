using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2a Task 4 三种签核模式的公开 HTTP E2E 回归线。
/// 每条用例使用独立宿主与纯 <c>user</c> provider,只观察发起、待办和审批响应。
/// </summary>
public class WorkflowM2RegressionTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task All_sign_stays_running_until_one_rejects_then_rejects_the_instance()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-m2-all-starter");
        var firstId = await AddUser(admin, "wf-m2-all-first");
        var secondId = await AddUser(admin, "wf-m2-all-second");
        var definitionId = await Publish(admin, "会签一票否决", ApprovalModel(firstId, secondId, "all"));

        var starter = await ClientFor(f, "wf-m2-all-starter");
        var first = await ClientFor(f, "wf-m2-all-first");
        var second = await ClientFor(f, "wf-m2-all-second");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var firstApprove = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, firstApprove.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            firstApprove.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var secondReject = await PostEnvelope(second, "/api/v1/workflow/task/reject", new { taskId });
        Assert.Equal(0, secondReject.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Rejected,
            secondReject.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        Assert.Equal(WorkflowErrorCode.TaskConflict,
            (await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());
        Assert.Equal(WorkflowErrorCode.TaskConflict,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Any_sign_first_approval_decides_the_instance()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-m2-any-starter");
        var firstId = await AddUser(admin, "wf-m2-any-first");
        var secondId = await AddUser(admin, "wf-m2-any-second");
        var definitionId = await Publish(admin, "或签先表态", ApprovalModel(firstId, secondId, "any"));

        var starter = await ClientFor(f, "wf-m2-any-starter");
        var first = await ClientFor(f, "wf-m2-any-first");
        var second = await ClientFor(f, "wf-m2-any-second");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var firstApprove = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, firstApprove.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            firstApprove.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.Equal(WorkflowErrorCode.TaskConflict,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Sequential_sign_promotes_exactly_one_next_approver_in_order()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-m2-seq-starter");
        var firstId = await AddUser(admin, "wf-m2-seq-first");
        var secondId = await AddUser(admin, "wf-m2-seq-second");
        var definitionId = await Publish(admin, "顺序会签", ApprovalModel(firstId, secondId, "seq"));

        var starter = await ClientFor(f, "wf-m2-seq-starter");
        var first = await ClientFor(f, "wf-m2-seq-first");
        var second = await ClientFor(f, "wf-m2-seq-second");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        Assert.Equal(1, TodoCount(await GetEnvelope(first, "/api/v1/workflow/task/todo?Current=1&Size=20")));
        Assert.Equal(0, TodoCount(await GetEnvelope(second, "/api/v1/workflow/task/todo?Current=1&Size=20")));
        Assert.Equal(WorkflowErrorCode.TaskConflict,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        var firstApprove = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, firstApprove.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Running,
            firstApprove.GetProperty("data").GetProperty("instanceStatus").GetInt32());
        Assert.Equal([secondId],
            firstApprove.GetProperty("data").GetProperty("newAssigneeUserIds")
                .EnumerateArray().Select(x => x.GetInt64()).ToArray());

        Assert.Equal((int)WfInstanceStatus.Approved,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    private static int TodoCount(JsonElement envelope) =>
        envelope.GetProperty("data").GetProperty("items").GetArrayLength();

    private static object ApprovalModel(long firstId, long secondId, string mode) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "approve",
                type = "approval",
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object>
                        {
                            ["userIds"] = new[] { firstId, secondId },
                        },
                    },
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
            account,
            password = Password,
            name = account,
            enabled = true,
            orgId = 1,
            roleIds = new[] { 2L },
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

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
