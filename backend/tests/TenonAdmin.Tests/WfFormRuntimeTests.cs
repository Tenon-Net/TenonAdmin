using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
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
        Assert.False(values.RootElement.TryGetProperty("defaultEditable", out _));

        var second = await Start(client, definitionId, InitialValues());
        await SetVariables(factory, second.GetProperty("instanceId").GetInt64(), """{"readonlyValue":"original"}""");
        var taskId = second.GetProperty("createdTaskId").GetInt64();

        await AssertFormError(client, taskId, """{"defaultEditable":"ok"}""", "editableRequired", "required");
        await AssertFormError(client, taskId,
            """{"editableRequired":123,"defaultEditable":"ok"}""", "editableRequired", "valueInvalid");
    }

    [Fact]
    public async Task Money_without_min_rejects_negative_on_start_and_resubmit()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var approverId = await AddUser(admin, "wf-money-approver");
        var definitionId = await Publish(admin, "金额非负", MoneyModel(approverId));
        var starter = await ClientFor(factory, "superAdmin");

        var negative = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            variablesJson = """{"amount":-1}""",
        });
        AssertFormError(negative, "amount", "rangeInvalid");

        var started = await Start(starter, definitionId, """{"amount":12.5}""");
        var instanceId = started.GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("createdTaskId").GetInt64();
        var approver = await ClientFor(factory, "wf-money-approver");
        Assert.Equal(0, (await PostEnvelope(approver, "/api/v1/workflow/task/return", new { taskId, comment = "办理意见" })).GetProperty("code").GetInt32());

        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new
        {
            instanceId,
            variablesJson = """{"amount":-0.01}""",
        });
        AssertFormError(resubmit, "amount", "rangeInvalid");
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
        var definitionId = await Publish(client, "清空单附件", AttachmentModel(
            multiple: false,
            required: false,
            formPerms: [new { field = "files", access = "editable" }]));
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
        Assert.Equal("leave", ParseValues(pendingData).GetProperty("category").GetString());
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
        Assert.Equal("原始标题", values.GetProperty("title").GetString());
        Assert.Equal("leave", values.GetProperty("category").GetString());
        // 发起人不是任一审批节点的办理人。字段只在每个审批节点都隐藏时才从详情拿掉,
        // 所以复审仍可读的 reviewer、初审未隐藏的 attachment 要留在表单里。
        Assert.Equal(1, values.GetProperty("reviewer").GetInt64());
        Assert.Equal(attachmentId, values.GetProperty("attachment").GetInt64());
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

    [Fact]
    public async Task Observer_sees_form_values_when_only_one_parallel_arm_hides_them()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var visibleId = await AddUser(admin, "wf-form-visible-observer");
        var hiddenId = await AddUser(admin, "wf-form-hidden-observer");
        var definitionId = await Publish(admin, "观察者可见金额", ObserverParallelModel(visibleId, hiddenId));
        var started = await Start(admin, definitionId, """{"amount":8000,"reason":"差旅"}""");
        var instanceId = started.GetProperty("instanceId").GetInt64();

        var observer = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        var values = ParseValues(observer.GetProperty("data"));
        Assert.Equal(8000, values.GetProperty("amount").GetDecimal());
        Assert.Equal("差旅", values.GetProperty("reason").GetString());

        var visible = await ClientFor(factory, "wf-form-visible-observer");
        var visibleDetail = await GetEnvelope(visible, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(8000, ParseValues(visibleDetail.GetProperty("data")).GetProperty("amount").GetDecimal());

        var hidden = await ClientFor(factory, "wf-form-hidden-observer");
        var hiddenDetail = await GetEnvelope(hidden, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(JsonValueKind.Null, hiddenDetail.GetProperty("data").GetProperty("variablesJson").ValueKind);
    }

    [Fact]
    public async Task Empty_permission_table_does_not_persist_amount_or_clear_protected_fields()
    {
        using var factory = new WorkflowAppFactory();
        var (admin, approver) = await ApproverPair(factory, "wf-form-empty-perm");
        var definitionId = await Publish(admin, "空权限表", AmountModel(approver.UserId, []));

        await AssertAmountStays(factory, admin, approver.Client, definitionId, """{"amount":1,"reason":"篡改"}""");
        await AssertAmountStays(factory, admin, approver.Client, definitionId, """{"amount":null,"reason":""}""");
        await AssertAmountStays(factory, admin, approver.Client, definitionId, """{"reason":"只改事由"}""");
    }

    [Fact]
    public async Task Missing_permission_table_does_not_persist_amount_changes()
    {
        using var factory = new WorkflowAppFactory();
        var (admin, approver) = await ApproverPair(factory, "wf-form-missing-perm");
        var definitionId = await Publish(admin, "缺权限表", AmountModel(approver.UserId, formPerms: null));

        await AssertAmountStays(factory, admin, approver.Client, definitionId, """{"amount":1,"reason":"篡改"}""");
    }

    [Fact]
    public async Task Partial_grant_updates_amount_and_keeps_reason()
    {
        using var factory = new WorkflowAppFactory();
        var (admin, approver) = await ApproverPair(factory, "wf-form-partial-perm");
        var definitionId = await Publish(admin, "部分授权", AmountModel(approver.UserId,
        [
            new { field = "amount", access = "editable" },
        ]));
        var started = await Start(admin, definitionId, """{"amount":8000,"reason":"差旅"}""");
        var instanceId = started.GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("createdTaskId").GetInt64();

        var rejected = await PostEnvelope(approver.Client, "/api/v1/workflow/task/approve", new
        {
            taskId,
            variablesJson = """{"amount":-1,"reason":"篡改"}""",
        });
        AssertFormError(rejected, "amount", "rangeInvalid");
        await AssertStoredAmount(factory, admin, instanceId, 8000, "差旅");

        var approved = await PostEnvelope(approver.Client, "/api/v1/workflow/task/approve", new
        {
            taskId,
            variablesJson = """{"amount":99.5,"reason":"篡改"}""",
        });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());
        await AssertStoredAmount(factory, admin, instanceId, 99.5m, "差旅");
    }

    [Fact]
    public async Task Readonly_and_hidden_fields_keep_server_values_when_echoed_or_tampered()
    {
        using var factory = new WorkflowAppFactory();
        var (admin, approver) = await ApproverPair(factory, "wf-form-readonly-perm");
        var definitionId = await Publish(admin, "只读隐藏", AmountModel(approver.UserId,
        [
            new { field = "amount", access = "readonly" },
            new { field = "reason", access = "hidden" },
        ]));

        await AssertAmountStays(factory, admin, approver.Client, definitionId, """{"amount":8000,"reason":"差旅"}""");
        await AssertAmountStays(factory, admin, approver.Client, definitionId, """{"amount":1,"reason":"篡改"}""");
    }

    [Fact]
    public async Task Editable_fields_update_and_still_validate()
    {
        using var factory = new WorkflowAppFactory();
        var (admin, approver) = await ApproverPair(factory, "wf-form-editable-perm");
        var definitionId = await Publish(admin, "明确可编辑", AmountModel(approver.UserId,
        [
            new { field = "amount", access = "editable" },
            new { field = "reason", access = "editable" },
        ]));
        var started = await Start(admin, definitionId, """{"amount":8000,"reason":"差旅"}""");
        var instanceId = started.GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("createdTaskId").GetInt64();

        var rejected = await PostEnvelope(approver.Client, "/api/v1/workflow/task/approve", new
        {
            taskId,
            variablesJson = """{"amount":-2,"reason":"退款"}""",
        });
        AssertFormError(rejected, "amount", "rangeInvalid");
        await AssertStoredAmount(factory, admin, instanceId, 8000, "差旅");

        var approved = await PostEnvelope(approver.Client, "/api/v1/workflow/task/approve", new
        {
            taskId,
            variablesJson = """{"amount":20.5,"reason":"改期"}""",
        });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());
        await AssertStoredAmount(factory, admin, instanceId, 20.5m, "改期");
    }

    [Fact]
    public async Task Reject_uses_the_same_field_write_rules_as_approve()
    {
        using var factory = new WorkflowAppFactory();
        var (admin, approver) = await ApproverPair(factory, "wf-form-reject-perm");
        var definitionId = await Publish(admin, "拒绝同规则", AmountModel(approver.UserId, []));
        var started = await Start(admin, definitionId, """{"amount":8000,"reason":"差旅"}""");
        var instanceId = started.GetProperty("instanceId").GetInt64();

        var rejected = await PostEnvelope(approver.Client, "/api/v1/workflow/task/reject", new
        {
            taskId = started.GetProperty("createdTaskId").GetInt64(),
            comment = "金额不对",
            variablesJson = """{"amount":1,"reason":null}""",
        });
        Assert.Equal(0, rejected.GetProperty("code").GetInt32());
        await AssertStoredAmount(factory, admin, instanceId, 8000, "差旅");

        var editableId = await Publish(admin, "拒绝可改金额", AmountModel(approver.UserId,
        [
            new { field = "amount", access = "editable" },
        ]));
        var editable = await Start(admin, editableId, """{"amount":8000,"reason":"差旅"}""");
        var editableInstanceId = editable.GetProperty("instanceId").GetInt64();
        var changed = await PostEnvelope(approver.Client, "/api/v1/workflow/task/reject", new
        {
            taskId = editable.GetProperty("createdTaskId").GetInt64(),
            comment = "改完再拒",
            variablesJson = """{"amount":15,"reason":"篡改"}""",
        });
        Assert.Equal(0, changed.GetProperty("code").GetInt32());
        await AssertStoredAmount(factory, admin, editableInstanceId, 15, "差旅");
    }

    [Fact]
    public async Task Start_and_legal_resubmit_still_replace_form_values()
    {
        using var factory = new WorkflowAppFactory();
        var (admin, approver) = await ApproverPair(factory, "wf-form-resubmit-perm");
        var definitionId = await Publish(admin, "发起重提", AmountModel(approver.UserId, []));

        var started = await Start(admin, definitionId, """{"amount":12.5,"reason":"出差"}""");
        var instanceId = started.GetProperty("instanceId").GetInt64();
        await AssertStoredAmount(factory, admin, instanceId, 12.5m, "出差");

        var returned = await PostEnvelope(approver.Client, "/api/v1/workflow/task/return", new
        {
            taskId = started.GetProperty("createdTaskId").GetInt64(),
            comment = "补充事由",
        });
        Assert.Equal(0, returned.GetProperty("code").GetInt32());

        var resubmitted = await PostEnvelope(admin, "/api/v1/workflow/instance/resubmit", new
        {
            instanceId,
            variablesJson = """{"amount":30,"reason":"改期"}""",
        });
        Assert.Equal(0, resubmitted.GetProperty("code").GetInt32());
        await AssertStoredAmount(factory, admin, instanceId, 30, "改期");
    }

    [Fact]
    public async Task Empty_permission_table_does_not_clear_optional_attachment()
    {
        using var factory = new WorkflowAppFactory();
        var client = await ClientFor(factory, "superAdmin");
        var attachmentId = (await InsertFiles(factory, 1))[0];
        var definitionId = await Publish(client, "空权限不清附件", AttachmentModel(multiple: false, required: false));
        var start = await Start(client, definitionId, JsonSerializer.Serialize(new { files = attachmentId }));
        var instanceId = start.GetProperty("instanceId").GetInt64();

        var approved = await PostEnvelope(client, "/api/v1/workflow/task/approve", new
        {
            taskId = start.GetProperty("createdTaskId").GetInt64(),
            variablesJson = """{"files":null}""",
        });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());

        var stored = await StoredVariables(factory, instanceId);
        using var values = JsonDocument.Parse(stored!);
        Assert.Equal(attachmentId, values.RootElement.GetProperty("files").GetInt64());
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

    private static object MoneyModel(long approverId) => new
    {
        version = 1,
        formSchema = new
        {
            version = 1,
            fields = new[] { new { key = "amount", label = "金额", type = "money", required = true } },
        },
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
                    assignee = new { provider = "user", @params = new { userIds = new[] { approverId } } },
                    mode = "any",
                    returnPolicy = "prev",
                },
                next = (object?)null,
            },
        },
    };

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

    private static object AttachmentModel(bool multiple, string? accept = null, bool required = true, object[]? formPerms = null)
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
        }, formPerms);
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

    private static object ObserverParallelModel(long visibleUserId, long hiddenUserId) => new
    {
        version = 1,
        formSchema = new
        {
            version = 1,
            fields = new object[]
            {
                new { key = "amount", label = "金额", type = "money", required = true },
                new { key = "reason", label = "事由", type = "textarea", required = true },
            },
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
                        id = "visible-arm",
                        name = "可见",
                        next = new
                        {
                            id = "visible-approval",
                            type = "approval",
                            name = "可见审批",
                            props = new
                            {
                                assignee = new { provider = "user", @params = new { userIds = new[] { visibleUserId } } },
                                mode = "any",
                                formPerms = new[]
                                {
                                    new { field = "amount", access = "readonly" },
                                    new { field = "reason", access = "readonly" },
                                },
                            },
                            next = (object?)null,
                        },
                    },
                    new
                    {
                        id = "hidden-arm",
                        name = "隐藏",
                        next = new
                        {
                            id = "hidden-approval",
                            type = "approval",
                            name = "隐藏审批",
                            props = new
                            {
                                assignee = new { provider = "user", @params = new { userIds = new[] { hiddenUserId } } },
                                mode = "any",
                                formPerms = new[]
                                {
                                    new { field = "amount", access = "hidden" },
                                    new { field = "reason", access = "hidden" },
                                },
                            },
                            next = (object?)null,
                        },
                    },
                },
                next = (object?)null,
            },
        },
    };

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

    private static object AmountModel(long approverId, object[]? formPerms) => new
    {
        version = 1,
        formSchema = new
        {
            version = 1,
            fields = new object[]
            {
                new { key = "amount", label = "金额", type = "money", required = true },
                new { key = "reason", label = "事由", type = "textarea", required = true },
            },
        },
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
                    assignee = new { provider = "user", @params = new { userIds = new[] { approverId } } },
                    mode = "any",
                    returnPolicy = "prev",
                    formPerms,
                },
                next = (object?)null,
            },
        },
    };

    private sealed record ApproverSession(HttpClient Client, long UserId);

    private static async Task<(HttpClient Admin, ApproverSession Approver)> ApproverPair(
        WorkflowAppFactory factory, string account)
    {
        var admin = await ClientFor(factory, "superAdmin");
        var userId = await AddUser(admin, account);
        return (admin, new ApproverSession(await ClientFor(factory, account), userId));
    }

    private static async Task AssertAmountStays(
        WorkflowAppFactory factory,
        HttpClient admin,
        HttpClient approver,
        long definitionId,
        string variablesJson)
    {
        var started = await Start(admin, definitionId, """{"amount":8000,"reason":"差旅"}""");
        var instanceId = started.GetProperty("instanceId").GetInt64();
        var approved = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new
        {
            taskId = started.GetProperty("createdTaskId").GetInt64(),
            variablesJson,
        });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());
        await AssertStoredAmount(factory, admin, instanceId, 8000, "差旅");
    }

    private static async Task AssertStoredAmount(
        WorkflowAppFactory factory,
        HttpClient admin,
        long instanceId,
        decimal amount,
        string reason)
    {
        var stored = await StoredVariables(factory, instanceId);
        using var database = JsonDocument.Parse(stored!);
        Assert.Equal(amount, database.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(reason, database.RootElement.GetProperty("reason").GetString());

        var api = await VariablesOf(admin, instanceId);
        using var response = JsonDocument.Parse(api!);
        Assert.Equal(amount, response.RootElement.GetProperty("amount").GetDecimal());
        if (response.RootElement.TryGetProperty("reason", out var visibleReason))
            Assert.Equal(reason, visibleReason.GetString());
    }

    private static async Task<string?> StoredVariables(WorkflowAppFactory factory, long instanceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var instance = await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().InSingleAsync(instanceId);
        return instance?.VariablesJson;
    }

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
