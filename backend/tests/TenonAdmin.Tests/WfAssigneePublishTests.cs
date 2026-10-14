using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 发布时审批/抄送必须有办理人来源;指定成员必须是仍启用的用户。
/// </summary>
public class WfAssigneePublishTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Approval_without_assignee_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AssertPublishRejected(admin, "审批缺办理人", Chain(Node("approval", "ap", props: new { })), "assigneeRequired", "ap");
    }

    [Fact]
    public async Task Approval_with_empty_user_ids_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AssertPublishRejected(
            admin,
            "指定成员为空",
            Chain(Node("approval", "ap", UserAssignee(Array.Empty<long>()))),
            "assigneeUsersRequired",
            "ap");
    }

    [Fact]
    public async Task Missing_user_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AssertPublishRejected(
            admin,
            "指定成员不存在",
            Chain(Node("approval", "ap", UserAssignee([999L]))),
            "assigneeUserUnavailable",
            "ap");
    }

    [Fact]
    public async Task Disabled_user_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var disabledId = await AddUser(admin, "wf-assignee-disabled");
        var disabled = await PutEnvelope(admin, $"/api/v1/sys/user/{disabledId}/enabled", new { enabled = false });
        Assert.Equal(0, disabled.GetProperty("code").GetInt32());

        await AssertPublishRejected(
            admin,
            "指定成员已停用",
            Chain(Node("approval", "ap", UserAssignee([disabledId]))),
            "assigneeUserUnavailable",
            "ap");
    }

    [Fact]
    public async Task Cc_with_empty_user_ids_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AssertPublishRejected(
            admin,
            "抄送成员为空",
            Chain(Node("cc", "cc1", UserAssignee(Array.Empty<long>()))),
            "assigneeUsersRequired",
            "cc1");
    }

    [Fact]
    public async Task Disabled_transfer_target_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var disabledId = await AddUser(admin, "wf-nobody-transfer-disabled");
        var disabled = await PutEnvelope(admin, $"/api/v1/sys/user/{disabledId}/enabled", new { enabled = false });
        Assert.Equal(0, disabled.GetProperty("code").GetInt32());

        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new { provider = "initiator", @params = new { } },
            ["nobody"] = "transfer",
            ["nobodyTransferUserId"] = disabledId,
        };
        await AssertPublishRejected(admin, "转交人已停用", Chain(Node("approval", "ap", props)), "assigneeUserUnavailable", "ap");
    }

    [Theory]
    [InlineData("leader")]
    [InlineData("initiator")]
    public async Task Non_user_assignee_sources_publish(string provider)
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var props = new { assignee = new { provider, @params = new { level = 1 } }, mode = "any" };
        var definitionId = await AddDefinition(admin, provider, Chain(Node("approval", "ap", props)));
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Enabled_user_and_cc_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var userId = await AddUser(admin, "wf-assignee-ok");
        var model = Chain(Node("approval", "ap", UserAssignee([userId]), Node("cc", "cc1", UserAssignee([userId]))));
        var definitionId = await AddDefinition(admin, "启用用户", model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
    }

    private static object UserAssignee(long[] userIds) => new
    {
        assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = userIds } },
        mode = "any",
    };

    private static object Chain(object first) => new
    {
        version = 1,
        root = new { id = "start", type = "start", name = "", next = first },
    };

    private static object Node(string type, string id, object props, object? next = null) => new
    {
        id,
        type,
        name = id,
        props,
        next,
    };

    private static async Task AssertPublishRejected(
        HttpClient admin, string name, object model, string reason, string nodeId)
    {
        var definitionId = await AddDefinition(admin, name, model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });
        Assert.Equal(WorkflowErrorCode.ModelInvalid, published.GetProperty("code").GetInt32());
        Assert.Equal(reason, published.GetProperty("args").GetProperty("reason").GetString());
        Assert.Equal(nodeId, published.GetProperty("args").GetProperty("nodeId").GetString());
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
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account, password = Password, name = account, enabled = true, orgId = 1, roleIds = new[] { 2L },
        });
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> AddDefinition(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        return added.GetProperty("data").GetInt64();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private static async Task<JsonElement> PutEnvelope(HttpClient client, string path, object body) =>
        await (await client.PutJson(path, body)).ReadEnvelope();
}
