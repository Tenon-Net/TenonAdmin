using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>拒绝和退回必须填写意见。空白和纯空格都不算。</summary>
public class WfCommentRequiredTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Reject_and_return_require_a_non_empty_comment()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var approverId = await AddUser(admin, "wf-comment-approver", "意见办理人");
        var definitionId = await Publish(admin, "意见必填", ApprovalModel(approverId));
        var starter = await ClientFor(f, "superAdmin");
        var approver = await ClientFor(f, "wf-comment-approver");

        var first = await Start(starter, definitionId);
        var firstTask = first.GetProperty("createdTaskId").GetInt64();
        var missing = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new { taskId = firstTask });
        Assert.Equal(WorkflowErrorCode.CommentRequired, missing.GetProperty("code").GetInt32());

        var blank = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new { taskId = firstTask, comment = "   " });
        Assert.Equal(WorkflowErrorCode.CommentRequired, blank.GetProperty("code").GetInt32());

        var rejected = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new { taskId = firstTask, comment = "不同意" });
        Assert.Equal(0, rejected.GetProperty("code").GetInt32());

        var second = await Start(starter, definitionId);
        var secondTask = second.GetProperty("createdTaskId").GetInt64();
        var returnMissing = await PostEnvelope(approver, "/api/v1/workflow/task/return", new { taskId = secondTask, comment = "" });
        Assert.Equal(WorkflowErrorCode.CommentRequired, returnMissing.GetProperty("code").GetInt32());

        var returned = await PostEnvelope(approver, "/api/v1/workflow/task/return", new { taskId = secondTask, comment = "请补充材料" });
        Assert.Equal(0, returned.GetProperty("code").GetInt32());
    }

    private static object ApprovalModel(long approverId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "approval",
                type = "approval",
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { approverId } },
                    },
                    mode = "any",
                    returnPolicy = "prev",
                },
            },
        },
    };

    private static async Task<JsonElement> Start(HttpClient client, long definitionId)
    {
        var started = await PostEnvelope(client, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        return started.GetProperty("data");
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

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account, string name)
    {
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account, password = Password, name, enabled = true, orgId = 1, roleIds = new[] { 2L },
        });
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
