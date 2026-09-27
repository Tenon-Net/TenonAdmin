using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfDefinitionService.ValidateModelForPublish"/> 的 branch 分支覆盖(经 HTTP 发布接口断言 <c>code</c>)。
/// 覆盖:合法 branch 模型发布成功 + 五类臂配置违规各一条 + 臂内节点 Id 与主链重复被拒。
/// </summary>
public class WfBranchPublishValidationTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Branch_model_with_valid_arms_publishes_successfully()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        var definitionId = await AddDefinition(admin, "合法分支", ValidBranchModel());
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(0, published.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Branch_publish_validation_rejects_each_known_violation()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        await AssertPublishRejected(admin, "分支无臂", BranchModel(conditions: null), "branchNoArms");
        await AssertPublishRejected(admin, "臂全默认", BranchModel(conditions:
        [
            new { id = "armA", name = "A", isDefault = true },
            new { id = "armB", name = "B", isDefault = true },
        ]), "branchDefaultArmCount");
        await AssertPublishRejected(admin, "臂 Id 为空", BranchModel(conditions:
        [
            new { id = "", name = "空 Id", isDefault = true },
        ]), "emptyArmId");
        await AssertPublishRejected(admin, "臂 Id 重复", BranchModel(conditions:
        [
            new { id = "armX", name = "默认", isDefault = true },
            new { id = "armX", name = "非默认", isDefault = false, expr = AmountGt(100) },
        ]), "duplicateArmId");
        await AssertPublishRejected(admin, "非默认臂缺条件", BranchModel(conditions:
        [
            new { id = "def", name = "默认", isDefault = true },
            new { id = "nodef", name = "非默认无条件", isDefault = false },
        ]), "branchArmWithoutExpr");
    }

    [Fact]
    public async Task Branch_arm_node_id_duplicate_with_main_chain_is_rejected()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        // armA 的子链节点 Id 复用主链的 "start",跨臂/主链共享的 seen 集合应当拦下它。
        var model = BranchModel(conditions:
        [
            new
            {
                id = "armA",
                name = "撞主链 Id",
                isDefault = false,
                expr = AmountGt(100),
                next = new
                {
                    id = "start",
                    type = "approval",
                    name = "撞 Id 节点",
                    props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                    next = (object?)null,
                },
            },
            new { id = "armB", name = "默认", isDefault = true },
        ]);

        await AssertPublishRejected(admin, "臂内节点撞主链 Id", model, "duplicateNodeId");
    }

    [Fact]
    public async Task Non_branch_node_with_conditions_is_rejected()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        // approval 节点不该携带 conditions;树化前的老校验被 Round 12 评审发现漏了这一刀
        // (子树 Id 撞主链、未注册 provider 都不会被拦,垃圾会被写进不可变的发布快照)。
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
                    id = "approval1",
                    type = "approval",
                    name = "审批",
                    props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                    conditions = new object[] { new { id = "x", name = "x", isDefault = true } },
                    next = (object?)null,
                },
            },
        };

        await AssertPublishRejected(admin, "非分支节点挂条件", model, "conditionsOnNonBranch");
    }

    private static async Task AssertPublishRejected(HttpClient admin, string name, object model, string expectedReason)
    {
        var definitionId = await AddDefinition(admin, name, model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(WorkflowErrorCode.ModelInvalid, published.GetProperty("code").GetInt32());
        Assert.Equal(expectedReason, published.GetProperty("args").GetProperty("reason").GetString());
    }

    private static object ValidBranchModel() => BranchModel(conditions:
    [
        new
        {
            id = "armHigh",
            name = "大额",
            isDefault = false,
            expr = AmountGt(100),
            next = ApprovalNode("high-approve"),
        },
        new { id = "armLow", name = "默认", isDefault = true },
    ]);

    /// <summary>root(start) → branch(<paramref name="conditions"/>) → merge-approve。</summary>
    private static object BranchModel(object[]? conditions) => new
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
                conditions,
                next = ApprovalNode("merge-approve"),
            },
        },
    };

    private static object ApprovalNode(string id) => new
    {
        id,
        type = "approval",
        name = id,
        props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
        next = (object?)null,
    };

    private static object AmountGt(int value) => new { field = "amount", op = "gt", value };

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
