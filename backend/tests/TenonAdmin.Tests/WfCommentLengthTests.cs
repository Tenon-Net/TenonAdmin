using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>人工意见最长 1000 字。拒绝和同意都按去空白后的长度计算。</summary>
public class WfCommentLengthTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Comments_are_capped_at_one_thousand_characters()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var approverId = await AddUser(admin, "wf-comment-length", "长意见办理人");
        var definitionId = await Publish(admin, "意见长度", ApprovalModel(approverId));
        var starter = await ClientFor(f, "superAdmin");
        var approver = await ClientFor(f, "wf-comment-length");

        var first = await Start(starter, definitionId);
        var firstTask = first.GetProperty("createdTaskId").GetInt64();
        var tooLong = new string('意', WfCommentRules.MaxLength + 1);
        var rejected = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new { taskId = firstTask, comment = tooLong });
        Assert.Equal(WorkflowErrorCode.CommentTooLong, rejected.GetProperty("code").GetInt32());
        Assert.Equal(WfCommentRules.MaxLength, rejected.GetProperty("args").GetProperty("maxLength").GetInt32());

        var accepted = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new
        {
            taskId = firstTask,
            comment = "  " + new string('意', WfCommentRules.MaxLength) + "  ",
        });
        Assert.Equal(0, accepted.GetProperty("code").GetInt32());

        var second = await Start(starter, definitionId);
        var secondTask = second.GetProperty("createdTaskId").GetInt64();
        var approveTooLong = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId = secondTask, comment = tooLong });
        Assert.Equal(WorkflowErrorCode.CommentTooLong, approveTooLong.GetProperty("code").GetInt32());

        var approved = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new
        {
            taskId = secondTask,
            comment = new string('同', WfCommentRules.MaxLength),
        });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());
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
