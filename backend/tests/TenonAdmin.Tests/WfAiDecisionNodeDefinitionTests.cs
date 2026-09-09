using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M3b-0 Task 06: AI Decision 节点定义只携带指令、变量白名单和既有人工兜底/重试配置；
/// 通过真实定义新增/发布边界钉住发布校验与 v1 加法兼容。
/// </summary>
public class WfAiDecisionNodeDefinitionTests
{
    private const string Password = "Test@123456";
    private const string DefaultInstructions = "审查申请内容。\n仅返回结构化建议。";
    private static readonly string[] DefaultInputFields = ["amount", "department"];

    [Fact]
    public async Task AiDecision_with_initiator_fallback_publishes()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");

        var definitionId = await AddDefinition(admin, "AI 人工兜底", AiDecisionModel());
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(0, published.GetProperty("code").GetInt32());
        var detail = await GetEnvelope(admin, $"/api/v1/workflow/definition/{definitionId}");
        Assert.Equal(1, detail.GetProperty("data").GetProperty("currentVersion").GetInt32());
    }

    [Theory]
    [InlineData("Webhook URL", "webhookUrl", "\"https://forbidden.invalid/ai-hook?token=endpoint-secret\"", "endpoint-secret")]
    [InlineData("Webhook 请求头", "webhookHeaders", "{\"Authorization\":\"Bearer header-secret\"}", "Bearer header-secret")]
    [InlineData("空办理人策略", "nobody", "\"autoPass\"", null)]
    [InlineData("发起范围", "initiatorScope", "[{\"type\":\"forbidden-scope-secret\",\"id\":1}]", "forbidden-scope-secret")]
    [InlineData("审批模式", "mode", "\"all\"", null)]
    [InlineData("会签比例", "allPassRatio", "75", null)]
    [InlineData("拒绝策略", "onReject", "\"terminate\"", null)]
    [InlineData("拒绝目标", "rejectToNodeId", "\"forbidden-reject-target-secret\"", "forbidden-reject-target-secret")]
    [InlineData("退回策略", "returnPolicy", "\"prev\"", null)]
    [InlineData("退回目标", "returnToNodeId", "\"forbidden-return-target-secret\"", "forbidden-return-target-secret")]
    [InlineData("超时配置", "timeout", "{\"hours\":24,\"action\":\"remind\"}", null)]
    [InlineData("按钮文案", "buttonLabels", "{\"approve\":\"forbidden-button-secret\"}", "forbidden-button-secret")]
    [InlineData("空办理人转办", "nobodyTransferUserId", "90210", null)]
    [InlineData("字段权限", "formPerms", "[]", null)]
    [InlineData("Webhook 方法", "webhookMethod", "\"DELETE\"", null)]
    [InlineData("Webhook 超时", "webhookTimeoutSeconds", "45", null)]
    [InlineData("Webhook 失败策略", "webhookOnFailure", "\"manual\"", null)]
    public async Task AiDecision_publish_rejects_non_approved_known_props_without_persisting_snapshot(
        string caseName,
        string propName,
        string jsonValue,
        string? forbiddenValue)
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var extraProps = new Dictionary<string, object?>
        {
            [propName] = JsonSerializer.Deserialize<JsonElement>(jsonValue),
        };

        await AssertPublishRejected(
            admin,
            $"AI 禁止配置-{caseName}",
            AiDecisionModel(extraProps: extraProps),
            WorkflowErrorCode.ModelInvalid,
            "aiPropsUnsupported",
            forbiddenValue);
    }

    [Fact]
    public async Task Legacy_v1_draft_with_approval_and_webhook_publishes_without_shape_drift()
    {
        const string legacyJson = """
            {"version":1,"root":{"id":"start","type":"start","name":"","next":{"id":"approval1","type":"approval","name":"approval","props":{"assignee":{"provider":"initiator","params":{}},"mode":"any","onReject":"terminate","returnPolicy":"prev","timeout":{"hours":12,"action":"remind"},"buttonLabels":{"approve":"approve"},"nobody":"block","formPerms":[]},"next":{"id":"webhook1","type":"webhook","name":"webhook","props":{"webhookUrl":"https://legacy.invalid/hook","webhookMethod":"POST","webhookHeaders":{"X-Legacy":"v1"},"webhookTimeoutSeconds":30,"webhookOnFailure":"fail","maxAttempts":3}}}}}
            """;
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var model = JsonSerializer.Deserialize<JsonElement>(legacyJson);

        var definitionId = await AddDefinition(admin, "旧 v1 审批与 Webhook", model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(0, published.GetProperty("code").GetInt32());
        Assert.Equal(1, published.GetProperty("data").GetInt32());

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/definition/{definitionId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        Assert.Equal(1, detail.GetProperty("data").GetProperty("currentVersion").GetInt32());

        var versions = await GetEnvelope(admin, $"/api/v1/workflow/definition/versions/{definitionId}");
        Assert.Equal(0, versions.GetProperty("code").GetInt32());
        var versionRows = versions.GetProperty("data");
        Assert.Equal(1, versionRows.GetArrayLength());
        Assert.Equal(1, versionRows[0].GetProperty("version").GetInt32());
        Assert.Equal(legacyJson, versionRows[0].GetProperty("modelJson").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WorkflowOptions.MinMaxAttempts)]
    [InlineData(WorkflowOptions.MaxMaxAttempts)]
    public async Task AiDecision_publish_accepts_null_and_boundary_attempt_budgets(int? maxAttempts)
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");

        var definitionId = await AddDefinition(admin, $"AI 合法重试-{maxAttempts?.ToString() ?? "null"}", AiDecisionModel(maxAttempts: maxAttempts));
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(0, published.GetProperty("code").GetInt32());
        Assert.Equal(1, published.GetProperty("data").GetInt32());
        var versions = await GetEnvelope(admin, $"/api/v1/workflow/definition/versions/{definitionId}");
        Assert.Equal(0, versions.GetProperty("code").GetInt32());
        var versionRows = versions.GetProperty("data");
        Assert.Equal(1, versionRows.GetArrayLength());
        var snapshot = Assert.IsType<WfModel>(WfModelJson.Deserialize(versionRows[0].GetProperty("modelJson").GetString()!));
        Assert.Equal(maxAttempts, snapshot.Root.Next?.Props?.MaxAttempts);
    }

    [Theory]
    [InlineData(WorkflowOptions.MinMaxAttempts - 1)]
    [InlineData(WorkflowOptions.MaxMaxAttempts + 1)]
    public async Task AiDecision_publish_rejects_attempt_budget_outside_closed_interval(int maxAttempts)
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");

        await AssertPublishRejected(
            admin,
            $"AI 越界重试-{maxAttempts}",
            AiDecisionModel(maxAttempts: maxAttempts),
            WorkflowErrorCode.ModelInvalid,
            "maxAttemptsOutOfRange");
    }

    [Fact]
    public async Task AiDecision_publish_rejects_invalid_instructions_without_echoing_them()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var tooLong = new string('i', 2001);
        const string control = "review\u0001instruction";

        await AssertPublishRejected(
            admin,
            "AI 缺失指令",
            AiDecisionModel(includeInstructions: false),
            WorkflowErrorCode.ModelInvalid,
            "aiInstructionsRequired");
        await AssertPublishRejected(
            admin,
            "AI 空白指令",
            AiDecisionModel(instructions: " \r\n\t "),
            WorkflowErrorCode.ModelInvalid,
            "aiInstructionsRequired");
        await AssertPublishRejected(
            admin,
            "AI 过长指令",
            AiDecisionModel(instructions: tooLong),
            WorkflowErrorCode.ModelInvalid,
            "aiInstructionsTooLong",
            tooLong);
        await AssertPublishRejected(
            admin,
            "AI 控制字符指令",
            AiDecisionModel(instructions: control),
            WorkflowErrorCode.ModelInvalid,
            "aiInstructionsControlChars",
            control);
    }

    [Fact]
    public async Task AiDecision_publish_rejects_invalid_input_fields_without_echoing_them()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var tooMany = Enumerable.Range(0, 33).Select(index => $"field{index}").ToArray();
        var tooLong = new string('f', 65);
        const string control = "input\u0001field";

        await AssertPublishRejected(
            admin,
            "AI 缺失输入字段",
            AiDecisionModel(includeInputFields: false),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldsRequired");
        await AssertPublishRejected(
            admin,
            "AI 空输入字段",
            AiDecisionModel(inputFields: []),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldsRequired");
        await AssertPublishRejected(
            admin,
            "AI 输入字段过多",
            AiDecisionModel(inputFields: tooMany),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldsTooMany",
            tooMany[^1]);
        await AssertPublishRejected(
            admin,
            "AI 空输入字段名",
            AiDecisionModel(inputFields: [""]),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldBlank");
        await AssertPublishRejected(
            admin,
            "AI 输入字段带空白",
            AiDecisionModel(inputFields: [" amount "]),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldPadded",
            " amount ");
        await AssertPublishRejected(
            admin,
            "AI 输入字段过长",
            AiDecisionModel(inputFields: [tooLong]),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldTooLong",
            tooLong);
        await AssertPublishRejected(
            admin,
            "AI 输入字段控制字符",
            AiDecisionModel(inputFields: [control]),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldControlChars",
            control);
        await AssertPublishRejected(
            admin,
            "AI 输入字段重复",
            AiDecisionModel(inputFields: ["amount", "amount"]),
            WorkflowErrorCode.ModelInvalid,
            "aiInputFieldDuplicate",
            "amount");
    }

    [Fact]
    public async Task AiDecision_publish_requires_a_registered_fallback_provider_and_valid_attempt_budget()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");

        await AssertPublishRejected(
            admin,
            "AI 缺失办理人",
            AiDecisionModel(assignee: new { }),
            WorkflowErrorCode.ModelInvalid,
            "aiAssigneeProviderRequired");
        await AssertPublishRejected(
            admin,
            "AI 未注册办理人",
            AiDecisionModel(assignee: Assignee("missing-ai-provider")),
            WorkflowErrorCode.ProviderNotRegistered,
            "aiAssigneeProviderNotRegistered");
        await AssertPublishRejected(
            admin,
            "AI 办理人过长",
            AiDecisionModel(assignee: Assignee(new string('p', 65))),
            WorkflowErrorCode.ModelInvalid,
            "aiAssigneeProviderTooLong");
        await AssertPublishRejected(
            admin,
            "AI 重试次数无效",
            AiDecisionModel(maxAttempts: 0),
            WorkflowErrorCode.ModelInvalid,
            "maxAttemptsOutOfRange");
        await AssertPublishRejected(
            admin,
            "AI 节点条件无效",
            AiDecisionModel(conditions: [new { id = "forbidden", name = "forbidden", isDefault = true }]),
            WorkflowErrorCode.ModelInvalid,
            "conditionsOnNonBranch");
    }

    [Fact]
    public void Schema_v1_legacy_models_without_ai_fields_keep_their_json_shape()
    {
        const string legacyJson = """
            {"version":1,"root":{"id":"start","type":"start","name":"","next":{"id":"approval1","type":"approval","name":"approval","props":{"assignee":{"provider":"initiator","params":{}},"mode":"any"}}}}
            """;

        var model = Assert.IsType<WfModel>(WfModelJson.Deserialize(legacyJson));
        var approvalProps = Assert.IsType<WfNodeProps>(model.Root.Next?.Props);

        Assert.Equal(1, model.Version);
        Assert.Null(approvalProps.AiInstructions);
        Assert.Null(approvalProps.AiInputFields);
        Assert.Equal(legacyJson, WfModelJson.Serialize(model));
        Assert.Equal(1, WfModelJson.CurrentVersion);
    }

    [Fact]
    public void Node_type_values_json_and_ai_props_are_additive_and_pinned()
    {
        var existing = new (WfNodeType Type, int Value, string Json)[]
        {
            (WfNodeType.Start, 0, "start"),
            (WfNodeType.Approval, 1, "approval"),
            (WfNodeType.Cc, 2, "cc"),
            (WfNodeType.Branch, 3, "branch"),
            (WfNodeType.Parallel, 4, "parallel"),
            (WfNodeType.Webhook, 5, "webhook"),
        };

        foreach (var nodeType in existing)
        {
            Assert.Equal(nodeType.Value, (int)nodeType.Type);
            Assert.Equal($"\"{nodeType.Json}\"", JsonSerializer.Serialize(nodeType.Type, WfModelJson.Options));
        }

        Assert.Equal(6, (int)WfNodeType.AiDecision);
        Assert.Equal("\"aiDecision\"", JsonSerializer.Serialize(WfNodeType.AiDecision, WfModelJson.Options));
        Assert.Equal(
            ["Start", "Approval", "Cc", "Branch", "Parallel", "Webhook", "AiDecision"],
            Enum.GetNames<WfNodeType>());

        var declaredProps = typeof(WfNodeProps)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        var aiPropNames = declaredProps.Keys
            .Where(name => name.StartsWith("Ai", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["AiInputFields", "AiInstructions"], aiPropNames);
        Assert.Equal(typeof(string), declaredProps["AiInstructions"].PropertyType);
        Assert.Equal(typeof(List<string>), declaredProps["AiInputFields"].PropertyType);
        foreach (var forbiddenName in new[] { "AutoApprove", "AutoReject", "ShadowMode", "Provider", "Model", "Endpoint", "ApiKey" })
            Assert.False(declaredProps.ContainsKey(forbiddenName), $"Unexpected public AI node prop: {forbiddenName}");
    }

    private static object AiDecisionModel(
        string? instructions = DefaultInstructions,
        string[]? inputFields = null,
        object? assignee = null,
        int? maxAttempts = null,
        object[]? conditions = null,
        bool includeInstructions = true,
        bool includeInputFields = true,
        IReadOnlyDictionary<string, object?>? extraProps = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = assignee ?? Assignee("initiator"),
        };
        if (includeInstructions)
            props["aiInstructions"] = instructions;
        if (includeInputFields)
            props["aiInputFields"] = inputFields ?? DefaultInputFields;
        if (maxAttempts.HasValue)
            props["maxAttempts"] = maxAttempts.Value;
        if (extraProps is not null)
        {
            foreach (var extraProp in extraProps)
                props[extraProp.Key] = extraProp.Value;
        }

        return new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = new
                {
                    id = "ai-decision",
                    type = "aiDecision",
                    name = "AI 审查",
                    props,
                    conditions,
                    next = (object?)null,
                },
            },
        };
    }

    private static object Assignee(string provider) => new { provider, @params = new { } };

    private static async Task AssertPublishRejected(
        HttpClient admin,
        string name,
        object model,
        int expectedCode,
        string expectedReason,
        string? forbiddenValue = null)
    {
        var definitionId = await AddDefinition(admin, name, model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });

        Assert.Equal(expectedCode, published.GetProperty("code").GetInt32());
        var args = published.GetProperty("args");
        Assert.Equal(expectedReason, args.GetProperty("reason").GetString());
        AssertJsonDoesNotContainString(args, DefaultInstructions);
        if (forbiddenValue is not null)
            AssertJsonDoesNotContainString(args, forbiddenValue);

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/definition/{definitionId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        Assert.Equal(0, detail.GetProperty("data").GetProperty("currentVersion").GetInt32());

        var versions = await GetEnvelope(admin, $"/api/v1/workflow/definition/versions/{definitionId}");
        Assert.Equal(0, versions.GetProperty("code").GetInt32());
        Assert.Equal(0, versions.GetProperty("data").GetArrayLength());
    }

    private static void AssertJsonDoesNotContainString(JsonElement element, string forbiddenValue)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    AssertJsonDoesNotContainString(property.Value, forbiddenValue);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    AssertJsonDoesNotContainString(item, forbiddenValue);
                break;
            case JsonValueKind.String:
                Assert.DoesNotContain(forbiddenValue, element.GetString() ?? "", StringComparison.Ordinal);
                break;
        }
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory factory, string account)
    {
        var client = factory.CreateClient();
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

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
