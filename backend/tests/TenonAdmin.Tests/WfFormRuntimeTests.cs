using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>内置表单发起与办理边界：字段权限、值类型、附件引用和旧定义兼容。</summary>
public class WfFormRuntimeTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Approval_drops_hidden_restores_readonly_and_validates_only_editable_fields()
    {
        using var factory = new WorkflowAppFactory();
        var client = await ClientFor(factory, "superAdmin");
        var definitionId = await Publish(client, "字段权限", PermissionModel());

        var first = await Start(client, definitionId, InitialValues());
        var firstInstanceId = first.GetProperty("instanceId").GetInt64();
        await SetVariables(factory, firstInstanceId, """{"readonlyValue":"original"}""");

        var approved = await PostEnvelope(client, "/api/v1/workflow/task/approve", new
        {
            taskId = first.GetProperty("createdTaskId").GetInt64(),
            variablesJson = """{"hiddenRequired":123,"readonlyRequired":123,"readonlyValue":123,"editableRequired":"changed","defaultEditable":"default-changed"}""",
        });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());

        var detail = await GetEnvelope(client, $"/api/v1/workflow/instance/{firstInstanceId}");
        using var values = JsonDocument.Parse(detail.GetProperty("data").GetProperty("variablesJson").GetString()!);
        Assert.False(values.RootElement.TryGetProperty("hiddenRequired", out _));
        Assert.False(values.RootElement.TryGetProperty("readonlyRequired", out _));
        Assert.Equal("original", values.RootElement.GetProperty("readonlyValue").GetString());
        Assert.Equal("changed", values.RootElement.GetProperty("editableRequired").GetString());
        Assert.Equal("default-changed", values.RootElement.GetProperty("defaultEditable").GetString());

        var second = await Start(client, definitionId, InitialValues());
        await SetVariables(factory, second.GetProperty("instanceId").GetInt64(), """{"readonlyValue":"original"}""");
        var taskId = second.GetProperty("createdTaskId").GetInt64();

        await AssertFormError(client, taskId, """{"defaultEditable":"ok"}""", "editableRequired", "required");
        await AssertFormError(client, taskId,
            """{"editableRequired":123,"defaultEditable":"ok"}""", "editableRequired", "valueInvalid");
    }

    [Fact]
    public async Task Start_rejects_values_with_the_wrong_schema_type()
    {
        using var factory = new WorkflowAppFactory();
        var client = await ClientFor(factory, "superAdmin");
        var definitionId = await Publish(client, "发起类型", TextModel());

        var response = await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            variablesJson = """{"title":123}""",
        });

        AssertFormError(response, "title", "valueInvalid");
    }

    [Fact]
    public async Task Start_accepts_decimal_string_user_and_attachment_ids()
    {
        using var factory = new WorkflowAppFactory();
        var client = await ClientFor(factory, "superAdmin");
        var attachmentId = (await InsertFiles(factory, 1))[0];
        var definitionId = await Publish(client, "字符串表单 ID", new
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
                    props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                },
            },
            formSchema = new
            {
                version = 1,
                fields = new object[]
                {
                    new { key = "reviewer", label = "人员", type = "user", required = true, props = new { multiple = false } },
                    new { key = "file", label = "附件", type = "attachment", required = true, props = new { multiple = false } },
                },
            },
        });

        var started = await Start(client, definitionId, $"{{\"reviewer\":\"1\",\"file\":\"{attachmentId}\"}}");
        Assert.True(started.GetProperty("instanceId").GetInt64() > 0);
    }

    [Fact]
    public async Task Attachment_enforces_default_multi_limit_single_shape_and_existing_file_ids()
    {
        using var factory = new WorkflowAppFactory();
        var client = await ClientFor(factory, "superAdmin");
        var ids = await InsertFiles(factory, 21);
        var multipleId = await Publish(client, "多附件", AttachmentModel(multiple: true));

        Assert.Equal(0, (await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = multipleId,
            variablesJson = JsonSerializer.Serialize(new { files = ids.Take(20) }),
        })).GetProperty("code").GetInt32());

        AssertFormError(await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = multipleId,
            variablesJson = JsonSerializer.Serialize(new { files = ids }),
        }), "files", "idInvalid");

        AssertFormError(await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = multipleId,
            variablesJson = """{"files":[999999999]}""",
        }), "attachment", "attachmentNotFound");

        var singleId = await Publish(client, "单附件", AttachmentModel(multiple: false));
        Assert.Equal(0, (await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = singleId,
            variablesJson = JsonSerializer.Serialize(new { files = ids[0] }),
        })).GetProperty("code").GetInt32());
        AssertFormError(await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = singleId,
            variablesJson = JsonSerializer.Serialize(new { files = ids.Take(1) }),
        }), "files", "idInvalid");

        var acceptedId = await Publish(client, "附件扩展名", AttachmentModel(multiple: false, accept: ".txt"));
        Assert.Equal(0, (await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = acceptedId,
            variablesJson = JsonSerializer.Serialize(new { files = ids[0] }),
        })).GetProperty("code").GetInt32());

        var rejectedId = await Publish(client, "附件扩展名拒绝", AttachmentModel(multiple: false, accept: ".pdf"));
        AssertFormError(await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = rejectedId,
            variablesJson = JsonSerializer.Serialize(new { files = ids[0] }),
        }), "files", "attachmentTypeInvalid");
    }

    [Fact]
    public async Task Approval_can_clear_existing_single_attachment_with_null()
    {
        using var factory = new WorkflowAppFactory();
        var client = await ClientFor(factory, "superAdmin");
        var attachmentId = (await InsertFiles(factory, 1))[0];
        var definitionId = await Publish(client, "清空单附件", AttachmentModel(multiple: false, required: false));
        var start = await Start(client, definitionId, JsonSerializer.Serialize(new { files = attachmentId }));

        var approved = await PostEnvelope(client, "/api/v1/workflow/task/approve", new
        {
            taskId = start.GetProperty("createdTaskId").GetInt64(),
            variablesJson = """{"files":null}""",
        });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());

        using var values = JsonDocument.Parse((await VariablesOf(
            client, start.GetProperty("instanceId").GetInt64()))!);
        Assert.Equal(JsonValueKind.Null, values.RootElement.GetProperty("files").ValueKind);
    }

    [Fact]
    public async Task Non_superadmin_cannot_submit_attachment_with_unknown_owner()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        await AddUser(admin, "wf-form-attachment-owner-check");
        var user = await ClientFor(factory, "wf-form-attachment-owner-check");
        var attachmentId = (await InsertFiles(factory, 1))[0];
        var definitionId = await Publish(admin, "附件所有者校验", AttachmentModel(multiple: false));

        AssertFormError(await PostEnvelope(user, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            variablesJson = JsonSerializer.Serialize(new { files = attachmentId }),
        }), "files", "attachmentNotAllowed");
    }

    [Fact]
    public async Task Definitions_without_builtin_schema_keep_valid_variables_unchanged()
    {
        using var factory = new WorkflowAppFactory();
        var client = await ClientFor(factory, "superAdmin");

        var noSchemaId = await Publish(client, "无 schema", LegacyModel());
        var noSchema = await Start(client, noSchemaId, "{\"legacy\":1}");
        Assert.Equal("{\"legacy\":1}", await VariablesOf(client, noSchema.GetProperty("instanceId").GetInt64()));

        var componentId = await Publish(client, "自定义表单", LegacyModel("views/legacy/form"));
        var component = await Start(client, componentId, "{\"legacy\":2}");
        Assert.Equal("{\"legacy\":2}", await VariablesOf(client, component.GetProperty("instanceId").GetInt64()));

        var invalid = await PostEnvelope(client, "/api/v1/workflow/instance/start", new
        {
            definitionId = componentId,
            variablesJson = "{legacy",
        });
        Assert.Equal(WorkflowErrorCode.FormValueInvalid, invalid.GetProperty("code").GetInt32());

        foreach (var variablesJson in new[] { "[1]", "{\"legacy\":1,\"legacy\":2}" })
        {
            var rejected = await PostEnvelope(client, "/api/v1/workflow/instance/start", new
            {
                definitionId = noSchemaId,
                variablesJson,
            });
            Assert.Equal(WorkflowErrorCode.FormValueInvalid, rejected.GetProperty("code").GetInt32());
        }

        var corrupted = await Start(client, noSchemaId, "{\"legacy\":3}");
        await SetVariables(factory, corrupted.GetProperty("instanceId").GetInt64(), "{\"legacy\":1,\"legacy\":2}");
        var readFailure = await GetEnvelope(client, $"/api/v1/workflow/instance/{corrupted.GetProperty("instanceId").GetInt64()}");
        Assert.Equal(WorkflowErrorCode.FormValueInvalid, readFailure.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Dynamic_form_vertical_slice_replays_two_approval_permissions()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var firstApproverId = await AddUser(admin, "wf-form-t10-first");
        var secondApproverId = await AddUser(admin, "wf-form-t10-second");
        var firstApprover = await ClientFor(factory, "wf-form-t10-first");
        var secondApprover = await ClientFor(factory, "wf-form-t10-second");
        var attachmentId = (await InsertFiles(factory, 1))[0];
        var definitionId = await Publish(admin, "动态表单验收", VerticalModel(attachmentId, firstApproverId, secondApproverId));

        var start = await Start(admin, definitionId, JsonSerializer.Serialize(new
        {
            title = "原始标题",
            category = "leave",
            reviewer = 1,
            attachment = attachmentId,
        }));
        var instanceId = start.GetProperty("instanceId").GetInt64();
        var firstTaskId = start.GetProperty("createdTaskId").GetInt64();

        var firstApproval = await PostEnvelope(firstApprover, "/api/v1/workflow/task/approve", new
        {
            taskId = firstTaskId,
            variablesJson = JsonSerializer.Serialize(new
            {
                title = "越权标题",
                category = "travel",
                reviewer = 999,
                attachment = attachmentId,
            }),
        });
        Assert.Equal(0, firstApproval.GetProperty("code").GetInt32());
        var secondTaskId = firstApproval.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var pendingDetail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        var pendingData = pendingDetail.GetProperty("data");
        Assert.Equal((int)WfInstanceStatus.Running, pendingData.GetProperty("status").GetInt32());
        Assert.Equal("approval2", pendingData.GetProperty("currentNodeIds")[0].GetString());
        Assert.Equal("原始标题", ParseValues(pendingData).GetProperty("title").GetString());
        Assert.Equal("travel", ParseValues(pendingData).GetProperty("category").GetString());
        Assert.Equal(1, ParseValues(pendingData).GetProperty("reviewer").GetInt64());

        var firstApproverDetail = await GetEnvelope(firstApprover, $"/api/v1/workflow/instance/{instanceId}");
        var firstApproverValues = ParseValues(firstApproverDetail.GetProperty("data"));
        Assert.False(firstApproverValues.TryGetProperty("reviewer", out _));
        Assert.Equal(attachmentId, firstApproverValues.GetProperty("attachment").GetInt64());

        var secondApproval = await PostEnvelope(secondApprover, "/api/v1/workflow/task/approve", new
        {
            taskId = secondTaskId,
            variablesJson = JsonSerializer.Serialize(new
            {
                title = "最终标题",
                category = "invalid-option",
                reviewer = 123,
                attachment = 999999999,
            }),
        });
        Assert.Equal(0, secondApproval.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            secondApproval.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var replay = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        var replayData = replay.GetProperty("data");
        var values = ParseValues(replayData);
        Assert.Equal("最终标题", values.GetProperty("title").GetString());
        Assert.Equal("travel", values.GetProperty("category").GetString());
        Assert.False(values.TryGetProperty("reviewer", out _));
        Assert.False(values.TryGetProperty("attachment", out _));
        Assert.Contains("approval1", replayData.GetProperty("visitedNodeIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("approval2", replayData.GetProperty("visitedNodeIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(2, replayData.GetProperty("hisTasks").GetArrayLength());
        Assert.Equal(4, replayData.GetProperty("model").GetProperty("formSchema").GetProperty("fields").GetArrayLength());
        Assert.Equal("hidden", replayData.GetProperty("model").GetProperty("root").GetProperty("next")
            .GetProperty("props").GetProperty("formPerms")[1].GetProperty("access").GetString());
    }

    [Fact]
    public async Task Instance_detail_uses_only_current_users_parallel_permissions()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var otherUserId = await AddUser(admin, "wf-form-parallel-other");
        var definitionId = await Publish(admin, "并行字段权限", ParallelPermissionModel(otherUserId));
        var start = await Start(admin, definitionId, """{"value":"visible"}""");

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{start.GetProperty("instanceId").GetInt64()}");
        var data = detail.GetProperty("data");
        Assert.Single(data.GetProperty("myPendingTasks").EnumerateArray());
        Assert.Equal("visible", ParseValues(data).GetProperty("value").GetString());
    }

    [Fact]
    public async Task Instance_detail_hides_field_when_own_parallel_permissions_conflict()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var definitionId = await Publish(admin, "并行字段权限收紧", ParallelPermissionModel(0, bothArmsForInitiator: true));
        var start = await Start(admin, definitionId, """{"value":"hidden"}""");

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{start.GetProperty("instanceId").GetInt64()}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("data").GetProperty("variablesJson").ValueKind);
    }

    private static object PermissionModel() => Model(new
    {
        version = 1,
        fields = new[]
        {
            new { key = "hiddenRequired", label = "隐藏必填", type = "text", required = true },
            new { key = "readonlyRequired", label = "只读必填", type = "text", required = true },
            new { key = "readonlyValue", label = "只读值", type = "text", required = false },
            new { key = "editableRequired", label = "可编辑必填", type = "text", required = true },
            new { key = "defaultEditable", label = "缺省可编辑", type = "text", required = true },
        },
    },
    [
        new { field = "hiddenRequired", access = "hidden" },
        new { field = "readonlyRequired", access = "readonly" },
        new { field = "readonlyValue", access = "readonly" },
        new { field = "editableRequired", access = "editable" },
    ]);

    private static object TextModel() => Model(new
    {
        version = 1,
        fields = new[] { new { key = "title", label = "标题", type = "text", required = true } },
    });

    private static object VerticalModel(long attachmentId, long firstApproverId, long secondApproverId) => new
    {
        version = 1,
        formSchema = new
        {
            version = 1,
            fields = new object[]
            {
                new { key = "title", label = "标题", type = "text", required = true },
                new
                {
                    key = "category", label = "类别", type = "select", required = true,
                    props = new { options = new[] { new { label = "请假", value = "leave" }, new { label = "出差", value = "travel" } } },
                },
                new { key = "reviewer", label = "人员", type = "user", required = true, props = new { multiple = false } },
                new { key = "attachment", label = $"附件-{attachmentId}", type = "attachment", required = true, props = new { multiple = false, maxCount = 1 } },
            },
        },
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "approval1",
                type = "approval",
                name = "初审",
                props = new
                {
                    assignee = new { provider = "user", @params = new { userIds = new[] { firstApproverId } } },
                    mode = "any",
                    formPerms = new[]
                    {
                        new { field = "title", access = "readonly" },
                        new { field = "reviewer", access = "hidden" },
                    },
                },
                next = new
                {
                    id = "approval2",
                    type = "approval",
                    name = "复审",
                    props = new
                    {
                        assignee = new { provider = "user", @params = new { userIds = new[] { secondApproverId } } },
                        mode = "any",
                        formPerms = new[]
                        {
                            new { field = "category", access = "readonly" },
                            new { field = "reviewer", access = "readonly" },
                            new { field = "attachment", access = "hidden" },
                        },
                    },
                    next = (object?)null,
                },
            },
        },
    };

    private static object AttachmentModel(bool multiple, string? accept = null, bool required = true)
    {
        var props = new Dictionary<string, object?> { ["multiple"] = multiple };
        if (accept is not null) props["accept"] = accept;
        return Model(new
        {
            version = 1,
            fields = new[]
            {
                new { key = "files", label = "附件", type = "attachment", required, props },
            },
        });
    }

    private static object ParallelPermissionModel(long otherUserId, bool bothArmsForInitiator = false)
    {
        object otherAssignee = bothArmsForInitiator
            ? new { provider = "initiator", @params = new { } }
            : new { provider = "user", @params = new { userIds = new[] { otherUserId } } };
        return new
        {
            version = 1,
            formSchema = new
            {
                version = 1,
                fields = new[] { new { key = "value", label = "值", type = "text", required = false } },
            },
            root = new
            {
                id = "start",
                type = "start",
                name = "发起",
                next = new
                {
                    id = "parallel",
                    type = "parallel",
                    name = "并行",
                    parallelArms = new object[]
                    {
                        new
                        {
                            id = "own-arm",
                            name = "本人",
                            next = new
                            {
                                id = "own-approval",
                                type = "approval",
                                name = "本人审批",
                                props = new
                                {
                                    assignee = new { provider = "initiator", @params = new { } },
                                    mode = "any",
                                    formPerms = new[] { new { field = "value", access = "editable" } },
                                },
                                next = (object?)null,
                            },
                        },
                        new
                        {
                            id = "other-arm",
                            name = "其他人",
                            next = new
                            {
                                id = "other-approval",
                                type = "approval",
                                name = "其他人审批",
                                props = new
                                {
                                    assignee = otherAssignee,
                                    mode = "any",
                                    formPerms = new[] { new { field = "value", access = "hidden" } },
                                },
                                next = (object?)null,
                            },
                        },
                    },
                    next = (object?)null,
                },
            },
        };
    }

    private static object LegacyModel(string? formComponent = null) => Model(
        formSchema: null,
        formPerms: [new { field = "legacyField", access = "hidden" }],
        formComponent: formComponent);

    private static object Model(object? formSchema, object[]? formPerms = null, string? formComponent = null) => new
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
                    assignee = new { provider = "initiator", @params = new { } },
                    mode = "any",
                    formPerms = formPerms ?? [],
                },
            },
        },
        formSchema,
        formComponent,
    };

    private static string InitialValues() =>
        """{"hiddenRequired":"hidden","readonlyRequired":"readonly","readonlyValue":"original","editableRequired":"editable","defaultEditable":"default"}""";

    private static JsonElement ParseValues(JsonElement detail) =>
        JsonDocument.Parse(detail.GetProperty("variablesJson").GetString()!).RootElement.Clone();

    private static async Task<JsonElement> Start(HttpClient client, long definitionId, string variablesJson)
    {
        var response = await PostEnvelope(client, "/api/v1/workflow/instance/start", new { definitionId, variablesJson });
        Assert.Equal(0, response.GetProperty("code").GetInt32());
        return response.GetProperty("data").Clone();
    }

    private static async Task AssertFormError(
        HttpClient client, long taskId, string variablesJson, string field, string reason) =>
        AssertFormError(await PostEnvelope(client, "/api/v1/workflow/task/approve", new { taskId, variablesJson }), field, reason);

    private static void AssertFormError(JsonElement response, string field, string reason)
    {
        Assert.Equal(WorkflowErrorCode.FormValueInvalid, response.GetProperty("code").GetInt32());
        Assert.Equal(field, response.GetProperty("args").GetProperty("field").GetString());
        Assert.Equal(reason, response.GetProperty("args").GetProperty("reason").GetString());
    }

    private static async Task SetVariables(WorkflowAppFactory factory, long instanceId, string variablesJson)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(1, await db.Updateable<WfInstance>()
            .SetColumns(_ => new WfInstance { VariablesJson = variablesJson })
            .Where(instance => instance.Id == instanceId)
            .ExecuteCommandAsync());
    }

    private static async Task<long[]> InsertFiles(WorkflowAppFactory factory, int count)
    {
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var files = Enumerable.Range(1, count).Select(index => new SysFile
        {
            Id = 9_000_000L + index,
            OriginalName = $"{index}.txt",
            StoragePath = $"tests/{index}.txt",
            Extension = ".txt",
            SizeBytes = 1,
        }).ToArray();
        await db.Insertable(files).ExecuteCommandAsync();
        return files.Select(file => file.Id).ToArray();
    }

    private static async Task<string?> VariablesOf(HttpClient client, long instanceId) =>
        (await GetEnvelope(client, $"/api/v1/workflow/instance/{instanceId}"))
        .GetProperty("data").GetProperty("variablesJson").GetString();

    private static async Task<long> Publish(HttpClient client, string name, object model)
    {
        var added = await PostEnvelope(client, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(client, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory factory, string account)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var response = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account,
            password = Password,
            name = account,
            enabled = true,
            orgId = 1,
            roleIds = new[] { 2L },
        });
        Assert.Equal(0, response.GetProperty("code").GetInt32());
        return response.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
