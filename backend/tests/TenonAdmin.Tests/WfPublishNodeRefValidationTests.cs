using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfDefinitionService.ValidateNodeReferences"/> 覆盖(经 HTTP 发布接口断言 <c>code</c>+<c>reason</c>):
/// <c>onReject=toNode</c> ⇒ <c>rejectToNodeId</c> 必须指向全树存在的节点;<c>returnPolicy=node</c> ⇒
/// <c>returnToNodeId</c> 同理。同时钉住<b>合法的前向引用与跨臂引用不能被误拒</b>——跳转目标可以在当前遍历
/// 位置之后、或在另一条分支臂上,所以这道校验必须独立走一趟整树索引,不能只查已遍历过的节点集合。
/// </summary>
public class WfPublishNodeRefValidationTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Reject_to_node_without_target_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertPublishRejected(
            admin,
            "拒绝路由缺目标",
            ChainModel(ApprovalNode("node1", onReject: "toNode")),
            "rejectToNodeIdInvalid");
    }

    [Fact]
    public async Task Reject_to_node_with_unknown_target_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertPublishRejected(
            admin,
            "拒绝路由目标不存在",
            ChainModel(ApprovalNode("node1", onReject: "toNode", rejectToNodeId: "ghost")),
            "rejectToNodeIdInvalid");
    }

    [Fact]
    public async Task Return_node_policy_without_target_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertPublishRejected(
            admin,
            "退回Node策略缺目标",
            ChainModel(ApprovalNode("node1", returnPolicy: "node")),
            "returnToNodeIdInvalid");
    }

    [Fact]
    public async Task Return_node_policy_with_unknown_target_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertPublishRejected(
            admin,
            "退回Node策略目标不存在",
            ChainModel(ApprovalNode("node1", returnPolicy: "node", returnToNodeId: "ghost")),
            "returnToNodeIdInvalid");
    }

    /// <summary>
    /// node1 的跳转目标 node2 排在它<b>之后</b>——顺序上「还没见过」,但整树里确实存在,必须放行。
    /// 若校验只查已遍历过的节点集合,这条会被误拒。
    /// </summary>
    [Fact]
    public async Task Forward_reference_to_later_node_publishes_successfully()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        var model = ChainModel(
            ApprovalNode("node1", onReject: "toNode", rejectToNodeId: "node2", next: ApprovalNode("node2")));
        var definitionId = await AddDefinition(admin, "前向引用", model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(0, published.GetProperty("code").GetInt32());
    }

    /// <summary>另一条分支臂上的节点作为跳转目标:同样必须放行。</summary>
    [Fact]
    public async Task Cross_arm_reference_publishes_successfully()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        var model = new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = new
                {
                    id = "branch1",
                    type = "branch",
                    name = "分支",
                    conditions = new object[]
                    {
                        new
                        {
                            id = "armHigh",
                            name = "大额",
                            isDefault = false,
                            expr = new { field = "amount", op = "gt", value = 100 },
                            // 目标在另一条臂(armLow)里,顺序上排在本臂之后。
                            next = ApprovalNode("high-approve", onReject: "toNode", rejectToNodeId: "low-approve"),
                        },
                        new
                        {
                            id = "armLow",
                            name = "默认",
                            isDefault = true,
                            next = ApprovalNode("low-approve"),
                        },
                    },
                    next = ApprovalNode("merge-approve"),
                },
            },
        };

        var definitionId = await AddDefinition(admin, "跨臂引用", model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(0, published.GetProperty("code").GetInt32());
    }

    private static async Task AssertPublishRejected(
        HttpClient admin,
        string name,
        object model,
        string expectedReason)
    {
        var definitionId = await AddDefinition(admin, name, model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(WorkflowErrorCode.ModelInvalid, published.GetProperty("code").GetInt32());
        Assert.Equal(expectedReason, published.GetProperty("args").GetProperty("reason").GetString());
    }

    private static object ChainModel(object firstNode) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = firstNode,
        },
    };

    private static object ApprovalNode(
        string id,
        string? onReject = null,
        string? rejectToNodeId = null,
        string? returnPolicy = null,
        string? returnToNodeId = null,
        object? next = null) => new
    {
        id,
        type = "approval",
        name = id,
        props = new
        {
            assignee = new { provider = "initiator", @params = new { } },
            mode = "any",
            onReject,
            rejectToNodeId,
            returnPolicy,
            returnToNodeId,
        },
        next,
    };

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddDefinition(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        return added.GetProperty("data").GetInt64();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
