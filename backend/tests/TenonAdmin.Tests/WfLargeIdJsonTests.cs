using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>工作流内嵌 JSON 只在公开输出边界把超出 JavaScript 安全范围的内置 Id 写成字符串。</summary>
public class WfLargeIdJsonTests
{
    private const string Password = "Test@123456";
    private const long SafeId = 9_007_199_254_740_991;
    private const long UnsafeId = 9_007_199_254_740_993;

    [Fact]
    public async Task Definition_outputs_stringify_only_unsafe_native_ids_and_keep_stored_model_numeric()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var model = ModelWithLargeIds();

        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new
        {
            name = "大编号输出边界",
            model,
        });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var definitionId = added.GetProperty("data").GetInt64();

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/definition/{definitionId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        AssertModelIdBoundary(detail.GetProperty("data").GetProperty("model"));

        using (var setupScope = factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var draft = await setupDb.Queryable<WfDefinitionVersion>()
                .Where(version => version.DefinitionId == definitionId && version.Version == 0)
                .SingleAsync();
            await setupDb.Insertable(new WfDefinitionVersion
            {
                DefinitionId = definitionId,
                Version = 1,
                ModelJson = draft.ModelJson,
                PublishTime = DateTime.Now,
                PublishUserId = 1,
            }).ExecuteCommandAsync();
            await setupDb.Updateable<WfDefinition>()
                .SetColumns(definition => new WfDefinition
                {
                    CurrentVersion = 1,
                    Status = WfDefinitionStatus.Published,
                })
                .Where(definition => definition.Id == definitionId)
                .ExecuteCommandAsync();
        }
        var versions = await GetEnvelope(admin, $"/api/v1/workflow/definition/versions/{definitionId}");
        Assert.Equal(0, versions.GetProperty("code").GetInt32());
        using var outputModel = JsonDocument.Parse(
            versions.GetProperty("data")[0].GetProperty("modelJson").GetString()!);
        AssertModelIdBoundary(outputModel.RootElement);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var stored = await db.Queryable<WfDefinitionVersion>()
            .Where(version => version.DefinitionId == definitionId)
            .OrderBy(version => version.Version)
            .ToListAsync();
        Assert.Equal(2, stored.Count);
        foreach (var version in stored)
        {
            using var storedModel = JsonDocument.Parse(version.ModelJson);
            var root = storedModel.RootElement;
            Assert.Equal(JsonValueKind.Number, root.GetProperty("nobodyTransferUserId").ValueKind);
            Assert.Equal(UnsafeId, root.GetProperty("nobodyTransferUserId").GetInt64());
            var parameters = ApprovalParams(root);
            Assert.Equal(UnsafeId, parameters.GetProperty("userIds")[1].GetInt64());
            Assert.Equal(UnsafeId, parameters.GetProperty("arbitraryAmount").GetInt64());
            Assert.Equal(UnsafeId, RoleParams(root).GetProperty("roleIds")[0].GetInt64());
            Assert.Equal(UnsafeId, PositionParams(root).GetProperty("positionId").GetInt64());
        }
    }

    [Fact]
    public async Task History_output_stringifies_native_payload_ids_without_rewriting_business_numbers()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var definitionId = await AddAndPublishSimpleDefinition(admin);
        var started = await PostEnvelope(admin, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        const string payload = """
            {
              "userIds":[9007199254740991,9007199254740993],
              "adminUserId":9007199254740993,
              "toUserId":9007199254740993,
              "forkId":9007199254740993,
              "parentTokenId":9007199254740993,
              "arms":[{"childTokenId":9007199254740993,"childEntryNodeVisitId":9007199254740993,"amount":9007199254740993}],
              "status":9007199254740993,
              "amount":9007199254740993
            }
            """;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Insertable(new WfHistory
            {
                InstanceId = instanceId,
                EventType = WfHistoryEventType.DuplicateApproverSkipped,
                NodeId = "approval",
                PayloadJson = payload,
                Sequence = 999,
            }).ExecuteCommandAsync();
        }

        var history = await GetEnvelope(admin, $"/api/v1/workflow/instance/history/{instanceId}");
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        var injected = Assert.Single(
            history.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("sequence").GetInt32() == 999);
        using var outputPayload = JsonDocument.Parse(injected.GetProperty("payloadJson").GetString()!);
        var root = outputPayload.RootElement;
        Assert.Equal(SafeId, root.GetProperty("userIds")[0].GetInt64());
        Assert.Equal(UnsafeId.ToString(), root.GetProperty("userIds")[1].GetString());
        Assert.Equal(UnsafeId.ToString(), root.GetProperty("adminUserId").GetString());
        Assert.Equal(UnsafeId.ToString(), root.GetProperty("toUserId").GetString());
        Assert.Equal(UnsafeId.ToString(), root.GetProperty("forkId").GetString());
        Assert.Equal(UnsafeId.ToString(), root.GetProperty("parentTokenId").GetString());
        Assert.Equal(UnsafeId.ToString(), root.GetProperty("arms")[0].GetProperty("childTokenId").GetString());
        Assert.Equal(
            UnsafeId.ToString(),
            root.GetProperty("arms")[0].GetProperty("childEntryNodeVisitId").GetString());
        Assert.Equal(UnsafeId, root.GetProperty("status").GetInt64());
        Assert.False(root.TryGetProperty("amount", out _));
        Assert.False(root.GetProperty("arms")[0].TryGetProperty("amount", out _));

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var storedPayload = await verificationDb.Queryable<WfHistory>()
            .Where(item => item.InstanceId == instanceId && item.Sequence == 999)
            .Select(item => item.PayloadJson)
            .SingleAsync();
        using var stored = JsonDocument.Parse(storedPayload!);
        Assert.Equal(UnsafeId, stored.RootElement.GetProperty("userIds")[1].GetInt64());
        Assert.Equal(UnsafeId, stored.RootElement.GetProperty("amount").GetInt64());
    }

    [Fact]
    public async Task Form_output_stringifies_user_and_attachment_ids_without_rewriting_number_fields()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var definitionId = await AddAndPublishFormDefinition(admin);
        var started = await PostEnvelope(admin, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var variablesJson = $$"""
            {"reviewers":[{{SafeId}},{{UnsafeId}}],"attachments":[{{UnsafeId}}],"amount":{{UnsafeId}}}
            """;

        using (var setupScope = factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await setupDb.Updateable<WfInstance>()
                .SetColumns(instance => new WfInstance { VariablesJson = variablesJson })
                .Where(instance => instance.Id == instanceId)
                .ExecuteCommandAsync();
        }

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        using var output = JsonDocument.Parse(detail.GetProperty("data").GetProperty("variablesJson").GetString()!);
        Assert.Equal(SafeId, output.RootElement.GetProperty("reviewers")[0].GetInt64());
        Assert.Equal(UnsafeId.ToString(), output.RootElement.GetProperty("reviewers")[1].GetString());
        Assert.Equal(UnsafeId.ToString(), output.RootElement.GetProperty("attachments")[0].GetString());
        Assert.Equal(UnsafeId, output.RootElement.GetProperty("amount").GetInt64());

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var storedJson = await verificationDb.Queryable<WfInstance>()
            .Where(instance => instance.Id == instanceId)
            .Select(instance => instance.VariablesJson)
            .SingleAsync();
        using var stored = JsonDocument.Parse(storedJson!);
        Assert.Equal(UnsafeId, stored.RootElement.GetProperty("reviewers")[1].GetInt64());
        Assert.Equal(UnsafeId, stored.RootElement.GetProperty("attachments")[0].GetInt64());
        Assert.Equal(UnsafeId, stored.RootElement.GetProperty("amount").GetInt64());
    }

    [Fact]
    public async Task Outbox_management_output_projects_envelope_ids_without_rewriting_fence_or_storage()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var row = await InsertOutboxAsync(db, WfOutboxStatus.Failed, "management");
        var custom = await InsertOutboxAsync(db, WfOutboxStatus.Failed, "management-custom", "consumer.custom");

        var page = await GetEnvelope(admin, "/api/v1/workflow/outbox/page");
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var item = Assert.Single(
            page.GetProperty("data").GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("id").GetInt64() == row.Id);
        using var output = JsonDocument.Parse(item.GetProperty("payloadJson").GetString()!);
        AssertExecutionOutboxBoundary(output.RootElement);
        var customItem = Assert.Single(
            page.GetProperty("data").GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("id").GetInt64() == custom.Id);
        using var customOutput = JsonDocument.Parse(customItem.GetProperty("payloadJson").GetString()!);
        Assert.Equal(UnsafeId, customOutput.RootElement.GetProperty("executionId").GetInt64());

        var storedJson = await db.Queryable<WfOutbox>()
            .Where(outbox => outbox.Id == row.Id)
            .Select(outbox => outbox.PayloadJson)
            .SingleAsync();
        AssertStoredExecutionOutbox(storedJson!);
    }

    [Fact]
    public async Task Outbox_transport_projects_envelope_ids_without_rewriting_fence_or_storage()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var row = await InsertOutboxAsync(db, WfOutboxStatus.Pending, "transport");
        var transport = new CapturingOutboxTransport();
        var dispatcher = new WfOutboxDispatcher(
            db,
            transport,
            new WorkflowOptions(),
            TimeProvider.System,
            NullLogger<WfOutboxDispatcher>.Instance);

        Assert.Equal(WfOutboxStatus.Dispatched, await dispatcher.RunAsync(row.Id, CancellationToken.None));
        var message = Assert.Single(transport.Messages);
        using var output = JsonDocument.Parse(message.PayloadJson!);
        AssertExecutionOutboxBoundary(output.RootElement);

        var custom = await InsertOutboxAsync(db, WfOutboxStatus.Pending, "transport-custom", "consumer.custom");
        Assert.Equal(WfOutboxStatus.Dispatched, await dispatcher.RunAsync(custom.Id, CancellationToken.None));
        using var customOutput = JsonDocument.Parse(transport.Messages[1].PayloadJson!);
        Assert.Equal(UnsafeId, customOutput.RootElement.GetProperty("executionId").GetInt64());

        var storedJson = await db.Queryable<WfOutbox>()
            .Where(outbox => outbox.Id == row.Id)
            .Select(outbox => outbox.PayloadJson)
            .SingleAsync();
        AssertStoredExecutionOutbox(storedJson!);
    }

    private static void AssertModelIdBoundary(JsonElement model)
    {
        Assert.Equal(UnsafeId.ToString(), model.GetProperty("nobodyTransferUserId").GetString());
        var parameters = ApprovalParams(model);
        Assert.Equal(SafeId, parameters.GetProperty("userIds")[0].GetInt64());
        Assert.Equal(UnsafeId.ToString(), parameters.GetProperty("userIds")[1].GetString());
        Assert.Equal(UnsafeId.ToString(), parameters.GetProperty("userId").GetString());
        Assert.Equal(UnsafeId, parameters.GetProperty("roleIds")[0].GetInt64());
        Assert.Equal(UnsafeId, parameters.GetProperty("roleId").GetInt64());
        Assert.Equal(UnsafeId, parameters.GetProperty("positionId").GetInt64());
        Assert.Equal(UnsafeId, parameters.GetProperty("orgId").GetInt64());
        Assert.Equal(JsonValueKind.Number, parameters.GetProperty("arbitraryAmount").ValueKind);
        Assert.Equal(UnsafeId, parameters.GetProperty("arbitraryAmount").GetInt64());

        var roleParameters = RoleParams(model);
        Assert.Equal(UnsafeId.ToString(), roleParameters.GetProperty("roleIds")[0].GetString());
        Assert.Equal(UnsafeId.ToString(), roleParameters.GetProperty("roleId").GetString());
        Assert.Equal(UnsafeId, roleParameters.GetProperty("userId").GetInt64());

        var positionParameters = PositionParams(model);
        Assert.Equal(UnsafeId.ToString(), positionParameters.GetProperty("positionId").GetString());
        Assert.Equal(UnsafeId.ToString(), positionParameters.GetProperty("orgId").GetString());
        Assert.Equal(UnsafeId, positionParameters.GetProperty("userId").GetInt64());

        var consumerParameters = ConsumerParams(model);
        Assert.Equal(UnsafeId, consumerParameters.GetProperty("userIds")[0].GetInt64());
        Assert.Equal(UnsafeId, consumerParameters.GetProperty("positionId").GetInt64());
    }

    private static void AssertExecutionOutboxBoundary(JsonElement payload)
    {
        foreach (var property in new[]
                 {
                     "executionId", "instanceId", "tokenId", "nodeVisitId", "definitionVersionId",
                 })
            Assert.Equal(UnsafeId.ToString(), payload.GetProperty(property).GetString());
        Assert.Equal(UnsafeId, payload.GetProperty("fence").GetInt64());
        Assert.Equal(UnsafeId, payload.GetProperty("businessAmount").GetInt64());
    }

    private static void AssertStoredExecutionOutbox(string payloadJson)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        Assert.Equal(UnsafeId, payload.RootElement.GetProperty("executionId").GetInt64());
        Assert.Equal(UnsafeId, payload.RootElement.GetProperty("instanceId").GetInt64());
        Assert.Equal(UnsafeId, payload.RootElement.GetProperty("tokenId").GetInt64());
        Assert.Equal(UnsafeId, payload.RootElement.GetProperty("nodeVisitId").GetInt64());
        Assert.Equal(UnsafeId, payload.RootElement.GetProperty("definitionVersionId").GetInt64());
        Assert.Equal(UnsafeId, payload.RootElement.GetProperty("fence").GetInt64());
        Assert.Equal(UnsafeId, payload.RootElement.GetProperty("businessAmount").GetInt64());
    }

    private static JsonElement ApprovalParams(JsonElement model) =>
        model.GetProperty("root").GetProperty("next").GetProperty("props")
            .GetProperty("assignee").GetProperty("params");

    private static JsonElement RoleParams(JsonElement model) =>
        model.GetProperty("root").GetProperty("next").GetProperty("next").GetProperty("props")
            .GetProperty("assignee").GetProperty("params");

    private static JsonElement PositionParams(JsonElement model) =>
        model.GetProperty("root").GetProperty("next").GetProperty("next").GetProperty("next")
            .GetProperty("props").GetProperty("assignee").GetProperty("params");

    private static JsonElement ConsumerParams(JsonElement model) =>
        model.GetProperty("root").GetProperty("next").GetProperty("next").GetProperty("next")
            .GetProperty("next").GetProperty("props").GetProperty("assignee").GetProperty("params");

    private static object ModelWithLargeIds() => new
    {
        version = 1,
        nobody = "transfer",
        nobodyTransferUserId = UnsafeId,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起人",
            next = new
            {
                id = "approval",
                type = "approval",
                name = "审批",
                props = new
                {
                    mode = "any",
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object>
                        {
                            ["userIds"] = new[] { SafeId, UnsafeId },
                            ["userId"] = UnsafeId,
                            ["roleIds"] = new[] { UnsafeId },
                            ["roleId"] = UnsafeId,
                            ["positionId"] = UnsafeId,
                            ["orgId"] = UnsafeId,
                            ["arbitraryAmount"] = UnsafeId,
                        },
                    },
                },
                next = new
                {
                    // 草稿允许暂时缺 Id；输出投影不能因索引尚未成立而漏掉参数。
                    id = "",
                    type = "approval",
                    name = "角色审批",
                    props = new
                    {
                        mode = "any",
                        assignee = new
                        {
                            provider = "role",
                            @params = new Dictionary<string, object>
                            {
                                ["roleIds"] = new[] { UnsafeId },
                                ["roleId"] = UnsafeId,
                                ["userId"] = UnsafeId,
                            },
                        },
                    },
                    next = new
                    {
                        // 草稿也可能临时重复 Id；两个节点都必须走输出投影。
                        id = "approval",
                        type = "approval",
                        name = "职位审批",
                        props = new
                        {
                            mode = "any",
                            assignee = new
                            {
                                provider = "position",
                                @params = new Dictionary<string, object>
                                {
                                    ["positionId"] = UnsafeId,
                                    ["orgId"] = UnsafeId,
                                    ["userId"] = UnsafeId,
                                },
                            },
                        },
                        next = new
                        {
                            id = "consumer-approval",
                            type = "approval",
                            name = "消费者审批",
                            props = new
                            {
                                mode = "any",
                                assignee = new
                                {
                                    provider = "consumerProvider",
                                    @params = new Dictionary<string, object>
                                    {
                                        ["userIds"] = new[] { UnsafeId },
                                        ["positionId"] = UnsafeId,
                                    },
                                },
                            },
                            next = (object?)null,
                        },
                    },
                },
            },
        },
    };

    private static async Task<long> AddAndPublishSimpleDefinition(HttpClient admin)
    {
        var model = new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "发起人",
                next = new
                {
                    id = "approval",
                    type = "approval",
                    name = "审批",
                    props = new
                    {
                        mode = "any",
                        assignee = new
                        {
                            provider = "user",
                            @params = new Dictionary<string, object>
                            {
                                ["userIds"] = new[] { 1L },
                            },
                        },
                    },
                    next = (object?)null,
                },
            },
        };
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new
        {
            name = "历史大编号输出边界",
            model,
        });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var definitionId = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return definitionId;
    }

    private static async Task<WfOutbox> InsertOutboxAsync(
        ISqlSugarClient db,
        WfOutboxStatus status,
        string suffix,
        string messageType = WfOutboxStore.MessageTypeNodeExecutionCompleted)
    {
        var instance = new WfInstance
        {
            DefinitionVersionId = 1,
            StarterUserId = 1,
            Status = WfInstanceStatus.Running,
            BusinessKey = $"large-id-outbox-{suffix}",
        };
        await db.Insertable(instance).ExecuteCommandAsync();
        var execution = new WfNodeExecution
        {
            ExecutionKey = Guid.NewGuid().ToString("N"),
            ScopeKey = WfIdentityHash.NormalizeScopeKey(null),
            InstanceId = instance.Id,
            TokenId = 1,
            NodeVisitId = 1,
            NodeId = "large-id",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = 1,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        var row = new WfOutbox
        {
            ExecutionId = execution.Id,
            MessageType = messageType,
            MessageKey = $"{execution.ExecutionKey}:{messageType}",
            PayloadJson = $$"""
                {"executionId":{{UnsafeId}},"instanceId":{{UnsafeId}},"tokenId":{{UnsafeId}},"nodeVisitId":{{UnsafeId}},"definitionVersionId":{{UnsafeId}},"fence":{{UnsafeId}},"businessAmount":{{UnsafeId}}}
                """,
            Status = status,
            AvailableAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletedAtUtc = status == WfOutboxStatus.Failed ? DateTime.UtcNow : null,
        };
        await db.Insertable(row).ExecuteCommandAsync();
        return row;
    }

    private static async Task<long> AddAndPublishFormDefinition(HttpClient admin)
    {
        var model = new
        {
            version = 1,
            formSchema = new
            {
                version = 1,
                fields = new object[]
                {
                    new
                    {
                        key = "reviewers", label = "人员", type = "user",
                        props = new { multiple = true },
                    },
                    new
                    {
                        key = "attachments", label = "附件", type = "attachment",
                        props = new { multiple = true },
                    },
                    new { key = "amount", label = "金额", type = "number" },
                },
            },
            root = new
            {
                id = "start",
                type = "start",
                name = "发起人",
                next = new
                {
                    id = "approval",
                    type = "approval",
                    name = "审批",
                    props = new
                    {
                        mode = "any",
                        assignee = new
                        {
                            provider = "user",
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { 1L } },
                        },
                    },
                    next = (object?)null,
                },
            },
        };
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new
        {
            name = "表单大编号输出边界",
            model,
        });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var definitionId = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id = definitionId });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return definitionId;
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory factory, string account)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private sealed class CapturingOutboxTransport : IWfOutboxTransport
    {
        public List<WfOutboxMessage> Messages { get; } = [];

        public Task<WfOutboxDispatchResult> DispatchAsync(
            WfOutboxMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(WfOutboxDispatchResult.Succeeded());
        }
    }
}
