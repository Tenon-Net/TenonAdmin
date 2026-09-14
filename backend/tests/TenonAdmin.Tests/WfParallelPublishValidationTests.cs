using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>T18 并行节点发布契约：臂结构、禁止嵌套及 rejectToNode 臂边界。</summary>
public class WfParallelPublishValidationTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Parallel_with_two_arms_including_an_empty_arm_publishes()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory);

        var id = await AddDefinition(admin, "合法并行", ParallelModel(
        [
            new { id = "armA", name = "A", next = ApprovalNode("approve-a") },
            new { id = "armB", name = "空臂", next = (object?)null },
        ]));

        Assert.Equal(0, (await Publish(admin, id)).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Parallel_rejects_invalid_arm_shapes_and_nesting()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory);

        await AssertRejected(admin, "并行臂不足", ParallelModel(
            [new { id = "armA", name = "A", next = (object?)null }]), "parallelArmCountInvalid");
        await AssertRejected(admin, "并行臂空 Id", ParallelModel(
        [
            new { id = "", name = "A", next = (object?)null },
            new { id = "armB", name = "B", next = (object?)null },
        ]), "emptyParallelArmId");
        await AssertRejected(admin, "并行臂重复 Id", ParallelModel(
        [
            new { id = "same", name = "A", next = (object?)null },
            new { id = "same", name = "B", next = (object?)null },
        ]), "duplicateParallelArmId");
        await AssertRejected(admin, "嵌套并行", ParallelModel(
        [
            new { id = "armA", name = "A", next = ParallelNode("nested") },
            new { id = "armB", name = "B", next = (object?)null },
        ]), "nestedParallel");
    }

    [Fact]
    public async Task Reject_to_node_must_stay_in_the_same_parallel_arm()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory);

        var sameArm = ParallelModel(
        [
            new { id = "armA", name = "A", next = ApprovalNode("a1", "a2", ApprovalNode("a2")) },
            new { id = "armB", name = "B", next = ApprovalNode("b1") },
        ]);
        var sameArmId = await AddDefinition(admin, "同臂跳转", sameArm);
        Assert.Equal(0, (await Publish(admin, sameArmId)).GetProperty("code").GetInt32());

        await AssertRejected(admin, "跨臂跳转", ParallelModel(
        [
            new { id = "armA", name = "A", next = ApprovalNode("a1", "b1") },
            new { id = "armB", name = "B", next = ApprovalNode("b1") },
        ]), "rejectToNodeOutsideParallelArm");

        var externalToArm = new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = ApprovalNode("outside", "a1", ParallelNode("parallel1",
                [
                    new { id = "armA", name = "A", next = ApprovalNode("a1") },
                    new { id = "armB", name = "B", next = (object?)null },
                ])),
            },
        };
        await AssertRejected(admin, "外部跳入并行臂", externalToArm, "rejectToNodeOutsideParallelArm");
    }

    private static object ParallelModel(object[] arms) => new
    {
        version = 1,
        root = new { id = "start", type = "start", name = "", next = ParallelNode("parallel1", arms) },
    };

    private static object ParallelNode(string id, object[]? arms = null) => new
    {
        id,
        type = "parallel",
        name = id,
        parallelArms = arms ??
        [
            new { id = "nestedA", name = "A", next = (object?)null },
            new { id = "nestedB", name = "B", next = (object?)null },
        ],
        next = (object?)null,
    };

    private static object ApprovalNode(string id, string? rejectToNodeId = null, object? next = null) => new
    {
        id,
        type = "approval",
        name = id,
        props = new
        {
            assignee = new { provider = "initiator", @params = new { } },
            mode = "any",
            onReject = rejectToNodeId is null ? "terminate" : "toNode",
            rejectToNodeId,
        },
        next,
    };

    private static async Task AssertRejected(HttpClient admin, string name, object model, string reason)
    {
        var id = await AddDefinition(admin, name, model);
        var result = await Publish(admin, id);
        Assert.Equal(WorkflowErrorCode.ModelInvalid, result.GetProperty("code").GetInt32());
        Assert.Equal(reason, result.GetProperty("args").GetProperty("reason").GetString());
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken("superAdmin", Password));
        return client;
    }

    private static async Task<long> AddDefinition(HttpClient client, string name, object model)
    {
        var result = await Post(client, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, result.GetProperty("code").GetInt32());
        return result.GetProperty("data").GetInt64();
    }

    private static Task<JsonElement> Publish(HttpClient client, long id) =>
        Post(client, "/api/v1/workflow/definition/publish", new { id });

    private static async Task<JsonElement> Post(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
