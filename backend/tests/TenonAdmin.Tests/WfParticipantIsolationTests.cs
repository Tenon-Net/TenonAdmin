using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 隔离用户回归:待办/已办/办理动词、实例读写与撤销、抄送、委托规则、附件预览下载,
/// 以及跨部门、跨机构。未授权拒绝;发起人、办理人、抄送人、监控、超管、委托人仍可做合法访问。
/// </summary>
public class WfParticipantIsolationTests
{
    private const string Password = "Test@123456";
    private const string Secret = "iso-secret-note";
    private const long OrgRoot = 1;
    private const long OrgTech = 3;
    private const long OrgFrontend = 4;
    private const long OrgBackend = 5;
    private const long OrgHr = 7;

    [Fact]
    public async Task Isolated_users_keep_participant_access_and_block_cross_scope()
    {
        using var factory = new WorkflowAppFactory();
        long fileRole, monitorOrgRole, monitorTreeRole, monitorAllRole, wideRole, delegationRole, plainRole;
        long starterId, principalId, delegateId, ccId, childApproverId;
        await using (var setup = factory.Services.CreateAsyncScope())
        {
        var sp = setup.ServiceProvider;

        fileRole = await CreateRole(sp, "iso-file", DataScopeType.Org,
            "POST:/api/v1/sys/file/upload",
            "GET:/api/v1/sys/file/page",
            "GET:/api/v1/sys/file/{id}/download");
        monitorOrgRole = await CreateRole(sp, "iso-mon-org", DataScopeType.Org,
            "GET:/api/v1/workflow/instance/monitor");
        monitorTreeRole = await CreateRole(sp, "iso-mon-tree", DataScopeType.OrgAndChildren,
            "GET:/api/v1/workflow/instance/monitor");
        monitorAllRole = await CreateRole(sp, "iso-mon-all", DataScopeType.All,
            "GET:/api/v1/workflow/instance/monitor");
        wideRole = await CreateRole(sp, "iso-wide", DataScopeType.All);
        delegationRole = await CreateRole(sp, "iso-deleg", DataScopeType.Org,
            "GET:/api/v1/workflow/delegation/page",
            "POST:/api/v1/workflow/delegation/add",
            "PUT:/api/v1/workflow/delegation/{id}",
            "DELETE:/api/v1/workflow/delegation/{id}");
        plainRole = await CreateRole(sp, "iso-plain", DataScopeType.Org);

        starterId = await AddUser(sp, "iso-starter", OrgHr, fileRole);
        principalId = await AddUser(sp, "iso-principal", OrgTech, fileRole);
        delegateId = await AddUser(sp, "iso-delegate", OrgTech, fileRole);
        ccId = await AddUser(sp, "iso-cc", OrgFrontend, fileRole);
        var outsiderHrId = await AddUser(sp, "iso-out-hr", OrgHr, fileRole);
        var outsiderBeId = await AddUser(sp, "iso-out-be", OrgBackend, fileRole);
        var wideId = await AddUser(sp, "iso-wide-user", OrgBackend, wideRole);
        var monitorHrId = await AddUser(sp, "iso-mon-hr", OrgHr, fileRole, monitorOrgRole);
        var monitorTechId = await AddUser(sp, "iso-mon-tech", OrgTech, monitorOrgRole);
        var monitorTreeId = await AddUser(sp, "iso-mon-tree", OrgTech, monitorTreeRole);
        var monitorAllId = await AddUser(sp, "iso-mon-all", OrgRoot, monitorAllRole);
        var delegationHrId = await AddUser(sp, "iso-deleg-hr", OrgHr, delegationRole);
        var delegationTechId = await AddUser(sp, "iso-deleg-tech", OrgTech, delegationRole);
        var childStarterId = await AddUser(sp, "iso-child-starter", OrgFrontend, plainRole);
        childApproverId = await AddUser(sp, "iso-child-approver", OrgFrontend, plainRole);
        _ = (outsiderHrId, outsiderBeId, wideId, monitorHrId, monitorTechId, monitorTreeId, monitorAllId, delegationHrId, delegationTechId, childStarterId);
        }

        var admin = await ClientFor(factory, "superAdmin");
        var starter = await ClientFor(factory, "iso-starter");
        var principal = await ClientFor(factory, "iso-principal");
        var delegateUser = await ClientFor(factory, "iso-delegate");
        var cc = await ClientFor(factory, "iso-cc");
        var outsiderHr = await ClientFor(factory, "iso-out-hr");
        var outsiderBe = await ClientFor(factory, "iso-out-be");
        var wide = await ClientFor(factory, "iso-wide-user");
        var monitorHr = await ClientFor(factory, "iso-mon-hr");
        var monitorTech = await ClientFor(factory, "iso-mon-tech");
        var monitorTree = await ClientFor(factory, "iso-mon-tree");
        var monitorAll = await ClientFor(factory, "iso-mon-all");
        var delegationHr = await ClientFor(factory, "iso-deleg-hr");
        var delegationTech = await ClientFor(factory, "iso-deleg-tech");
        var childStarter = await ClientFor(factory, "iso-child-starter");
        var childApprover = await ClientFor(factory, "iso-child-approver");

        var windowStart = DateTime.UtcNow.AddMinutes(-2);
        var windowEnd = DateTime.UtcNow.AddHours(2);
        var addedRule = await Post(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = principalId,
            delegateUserId = delegateId,
            enabled = true,
            startsAt = windowStart,
            endsAt = windowEnd,
            requestId = "iso-delegation-add",
        });
        Assert.Equal(0, CodeOf(addedRule));
        var ruleId = addedRule.GetProperty("data").GetProperty("id").GetInt64();

        var (fileId, fileBytes, viewUrl) = await UploadPng(starter);
        var mainDefinitionId = await Publish(admin, "隔离-主流程", MainModel(ccId, principalId));
        var childDefinitionId = await Publish(admin, "隔离-子机构", ApprovalModel(childApproverId));

        var main = await Post(starter, "/api/v1/workflow/instance/start", new
        {
            definitionId = mainDefinitionId,
            businessKey = "iso-main",
            variablesJson = "{\"note\":\"" + Secret + "\",\"file\":" + fileId + "}",
        });
        Assert.Equal(0, CodeOf(main));
        var mainId = main.GetProperty("data").GetProperty("instanceId").GetInt64();

        var child = await Post(childStarter, "/api/v1/workflow/instance/start", new
        {
            definitionId = childDefinitionId,
            businessKey = "iso-child",
        });
        Assert.Equal(0, CodeOf(child));
        var childId = child.GetProperty("data").GetProperty("instanceId").GetInt64();

        var delegateTodo = await Get(delegateUser, "/api/v1/workflow/task/todo?Current=1&Size=20");
        Assert.Equal(0, CodeOf(delegateTodo));
        var mainTask = Assert.Single(delegateTodo.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(mainId, mainTask.GetProperty("instanceId").GetInt64());
        var taskId = mainTask.GetProperty("taskId").GetInt64();
        Assert.Contains(Secret, mainTask.GetProperty("variablesJson").GetString());
        Assert.DoesNotContain("sig=", mainTask.GetProperty("variablesJson").GetString());

        Assert.Empty(Ids(await Get(principal, "/api/v1/workflow/task/todo?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(starter, "/api/v1/workflow/task/todo?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(outsiderHr, "/api/v1/workflow/task/todo?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(outsiderBe, "/api/v1/workflow/task/todo?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(wide, "/api/v1/workflow/task/todo?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(cc, "/api/v1/workflow/task/todo?Current=1&Size=20"), "instanceId"));
        AssertSame([childId], Ids(await Get(childApprover, "/api/v1/workflow/task/todo?Current=1&Size=20"), "instanceId"));

        foreach (var client in new[] { starter, principal, delegateUser, cc, outsiderHr, outsiderBe, wide, monitorHr, childApprover })
            Assert.Empty(Ids(await Get(client, "/api/v1/workflow/task/done?Current=1&Size=20"), "instanceId"));

        AssertSame([mainId], Ids(await Get(starter, "/api/v1/workflow/instance/page?Current=1&Size=20"), "id"));
        AssertSame([childId], Ids(await Get(childStarter, "/api/v1/workflow/instance/page?Current=1&Size=20"), "id"));
        Assert.Empty(Ids(await Get(principal, "/api/v1/workflow/instance/page?Current=1&Size=20"), "id"));
        Assert.Empty(Ids(await Get(delegateUser, "/api/v1/workflow/instance/page?Current=1&Size=20"), "id"));
        Assert.Empty(Ids(await Get(outsiderHr, "/api/v1/workflow/instance/page?Current=1&Size=20"), "id"));
        Assert.Empty(Ids(await Get(monitorHr, "/api/v1/workflow/instance/page?Current=1&Size=20"), "id"));

        var readers = new (HttpClient Client, string Name, bool Main, bool Child)[]
        {
            (starter, "starter", true, false),
            (principal, "principal", true, false),
            (delegateUser, "delegate", true, false),
            (cc, "cc", true, false),
            (childStarter, "childStarter", false, true),
            (childApprover, "childApprover", false, true),
            (outsiderHr, "outsiderHr", false, false),
            (outsiderBe, "outsiderBe", false, false),
            (wide, "wideAllScope", false, false),
            (monitorHr, "monitorHr", true, false),
            (monitorTech, "monitorTech", false, false),
            (monitorTree, "monitorTechTree", false, true),
            (monitorAll, "monitorAll", true, true),
            (admin, "superAdmin", true, true),
            (delegationTech, "delegationTech", false, false),
            (delegationHr, "delegationHr", false, false),
        };
        foreach (var (client, name, canMain, canChild) in readers)
        {
            await AssertInstance(client, name, mainId, canMain, expectSecret: canMain);
            await AssertInstance(client, name, childId, canChild, expectSecret: false);
        }

        var ccPage = await Get(cc, "/api/v1/workflow/cc/page?Current=1&Size=20");
        var ccRow = Assert.Single(ccPage.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(mainId, ccRow.GetProperty("instanceId").GetInt64());
        var ccRowId = ccRow.GetProperty("id").GetInt64();
        Assert.Empty(Ids(await Get(outsiderHr, "/api/v1/workflow/cc/page?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(delegateUser, "/api/v1/workflow/cc/page?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(starter, "/api/v1/workflow/cc/page?Current=1&Size=20"), "instanceId"));
        Assert.Equal(WorkflowErrorCode.CcNotFound, CodeOf(await Post(outsiderHr, "/api/v1/workflow/cc/read", new { id = ccRowId })));
        Assert.Equal(WorkflowErrorCode.CcNotFound, CodeOf(await Post(principal, "/api/v1/workflow/cc/read", new { id = ccRowId })));
        Assert.Equal(0, CodeOf(await Post(cc, "/api/v1/workflow/cc/read", new { id = ccRowId })));

        const string monitorUrl = "/api/v1/workflow/instance/monitor?Current=1&Size=20";
        foreach (var client in new[] { starter, principal, delegateUser, cc, outsiderHr, outsiderBe, wide, childStarter, delegationHr, delegationTech })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(monitorUrl)).StatusCode);
        AssertSame([mainId], Ids(await Get(monitorHr, monitorUrl), "id"));
        Assert.Empty(Ids(await Get(monitorTech, monitorUrl), "id"));
        AssertSame([childId], Ids(await Get(monitorTree, monitorUrl), "id"));
        AssertSame([mainId, childId], Ids(await Get(monitorAll, monitorUrl), "id"));
        AssertSame([mainId, childId], Ids(await Get(admin, monitorUrl), "id"));

        var techRules = Ids(await Get(delegationTech, "/api/v1/workflow/delegation/page?Current=1&Size=20"), "id");
        Assert.Contains(ruleId, techRules);
        Assert.DoesNotContain(ruleId, Ids(await Get(delegationHr, "/api/v1/workflow/delegation/page?Current=1&Size=20"), "id"));
        Assert.DoesNotContain(ruleId, Ids(await Get(delegationHr,
            $"/api/v1/workflow/delegation/page?Current=1&Size=20&OriginalUserId={principalId}"), "id"));
        Assert.Contains(ruleId, Ids(await Get(admin, "/api/v1/workflow/delegation/page?Current=1&Size=50"), "id"));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderHr.GetAsync("/api/v1/workflow/delegation/page?Current=1&Size=20")).StatusCode);

        await AssertFileHidden(outsiderHr, fileId);
        await AssertFileHidden(outsiderBe, fileId);
        await AssertFileHidden(principal, fileId);
        await AssertFileHidden(delegateUser, fileId);
        await AssertFileHidden(cc, fileId);
        await AssertFileHidden(monitorHr, fileId);
        await AssertFileBytes(starter, fileId, fileBytes);
        await AssertFileBytes(admin, fileId, fileBytes);
        Assert.Contains(fileId, Ids(await Get(starter, "/api/v1/sys/file/page?Current=1&Size=50"), "id"));
        Assert.Contains(fileId, Ids(await Get(admin, "/api/v1/sys/file/page?Current=1&Size=50"), "id"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/v1/sys/file/{fileId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().GetAsync($"/api/v1/sys/file/{fileId}/view")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderBe.GetAsync($"/api/v1/sys/file/{fileId}/view?sig=tampered")).StatusCode);
        var storagePath = (await Get(starter, "/api/v1/sys/file/page?Current=1&Size=50"))
            .GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetInt64() == fileId)
            .GetProperty("storagePath").GetString()!;
        var anonymous = factory.CreateClient();
        foreach (var path in new[] { "/upload/" + storagePath, "/wwwroot/upload/" + storagePath, "/" + storagePath })
        {
            var leaked = await (await anonymous.GetAsync(path)).Content.ReadAsByteArrayAsync();
            Assert.NotEqual(fileBytes, leaked);
        }
        _ = viewUrl;

        Assert.Equal(WorkflowErrorCode.TaskConflict, CodeOf(await Post(outsiderHr, "/api/v1/workflow/task/transfer", new { taskId, toUserId = starterId })));
        Assert.Equal(WorkflowErrorCode.TaskConflict, CodeOf(await Post(outsiderHr, "/api/v1/workflow/task/delegate", new { taskId, toUserId = starterId })));

        foreach (var client in new[] { outsiderHr, outsiderBe, wide, principal, cc, monitorHr, childApprover, starter })
        {
            Assert.Equal(WorkflowErrorCode.TaskConflict, CodeOf(await Post(client, "/api/v1/workflow/task/approve", new { taskId })));
            Assert.Equal(WorkflowErrorCode.TaskConflict, CodeOf(await Post(client, "/api/v1/workflow/task/reject", new { taskId })));
            Assert.Equal(WorkflowErrorCode.TaskConflict, CodeOf(await Post(client, "/api/v1/workflow/task/return", new { taskId })));
            // 目标用户不在调用者数据范围内时,转办/委托会先返回目标非法,到不了办理人 CAS。两种都是拒绝。
            var transferred = CodeOf(await Post(client, "/api/v1/workflow/task/transfer", new { taskId, toUserId = childApproverId }));
            Assert.Contains(transferred, new[] { WorkflowErrorCode.TaskConflict, WorkflowErrorCode.TransferTargetInvalid });
            var delegated = CodeOf(await Post(client, "/api/v1/workflow/task/delegate", new { taskId, toUserId = childApproverId }));
            Assert.Contains(delegated, new[] { WorkflowErrorCode.TaskConflict, WorkflowErrorCode.DelegateTargetInvalid });
        }
        Assert.Equal(WorkflowErrorCode.UrgeNotAllowed, CodeOf(await Post(outsiderHr, "/api/v1/workflow/task/urge", new { taskId })));
        Assert.Equal(WorkflowErrorCode.UrgeNotAllowed, CodeOf(await Post(delegateUser, "/api/v1/workflow/task/urge", new { taskId })));
        Assert.Equal(WorkflowErrorCode.UrgeNotAllowed, CodeOf(await Post(monitorHr, "/api/v1/workflow/task/urge", new { taskId })));
        Assert.Equal(0, CodeOf(await Post(starter, "/api/v1/workflow/task/urge", new { taskId })));
        AssertSame([taskId], (await Get(delegateUser, "/api/v1/workflow/task/todo?Current=1&Size=20"))
            .GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("taskId").GetInt64()));

        var cancelStart = await Post(starter, "/api/v1/workflow/instance/start", new
        {
            definitionId = mainDefinitionId,
            businessKey = "iso-cancel",
            variablesJson = "{\"note\":\"cancel-me\",\"file\":" + fileId + "}",
        });
        Assert.Equal(0, CodeOf(cancelStart));
        var cancelId = cancelStart.GetProperty("data").GetProperty("instanceId").GetInt64();
        foreach (var client in new[] { outsiderHr, delegateUser, principal, monitorHr, cc, wide })
            Assert.Equal(WorkflowErrorCode.CancelNotAllowed, CodeOf(await Post(client, "/api/v1/workflow/instance/cancel", new { instanceId = cancelId })));
        Assert.Equal(0, CodeOf(await Post(starter, "/api/v1/workflow/instance/cancel", new { instanceId = cancelId })));
        var cancelled = await Get(starter, $"/api/v1/workflow/instance/{cancelId}");
        Assert.Equal(0, CodeOf(cancelled));
        Assert.Equal((int)WfInstanceStatus.Cancelled, cancelled.GetProperty("data").GetProperty("status").GetInt32());
        Assert.Equal(WorkflowErrorCode.InstanceAccessDenied, CodeOf(await Get(outsiderHr, $"/api/v1/workflow/instance/{cancelId}")));

        var approved = await Post(delegateUser, "/api/v1/workflow/task/approve", new { taskId, comment = "ok" });
        Assert.Equal(0, CodeOf(approved));
        AssertSame([mainId], Ids(await Get(delegateUser, "/api/v1/workflow/task/done?Current=1&Size=20"), "instanceId"));
        Assert.Empty(Ids(await Get(outsiderHr, "/api/v1/workflow/task/done?Current=1&Size=20"), "instanceId"));
        Assert.DoesNotContain(mainId, Ids(await Get(childApprover, "/api/v1/workflow/task/done?Current=1&Size=20"), "instanceId"));
        await AssertInstance(principal, "principal-after", mainId, canRead: true, expectSecret: true);
        await AssertInstance(delegateUser, "delegate-after", mainId, canRead: true, expectSecret: true);
        await AssertInstance(starter, "starter-after", mainId, canRead: true, expectSecret: true);
        await AssertInstance(outsiderBe, "outsider-after", mainId, canRead: false, expectSecret: false);

        Assert.Equal(WorkflowErrorCode.DelegationScopeDenied, CodeOf(await Put(delegationHr, $"/api/v1/workflow/delegation/{ruleId}", new
        {
            originalUserId = principalId,
            delegateUserId = delegateId,
            enabled = true,
            startsAt = windowStart,
            endsAt = windowEnd,
            requestId = "iso-cross-org-update",
        })));
        _ = starterId;
    }

    private static async Task AssertInstance(HttpClient client, string name, long instanceId, bool canRead, bool expectSecret)
    {
        var detail = await Get(client, $"/api/v1/workflow/instance/{instanceId}");
        var history = await Get(client, $"/api/v1/workflow/instance/history/{instanceId}");
        if (canRead)
        {
            Assert.True(CodeOf(detail) == 0, $"{name} detail {CodeOf(detail)} instance {instanceId}");
            Assert.True(CodeOf(history) == 0, $"{name} history {CodeOf(history)} instance {instanceId}");
            var variables = detail.GetProperty("data").GetProperty("variablesJson").GetString() ?? "";
            if (expectSecret)
            {
                Assert.Contains(Secret, variables);
                Assert.DoesNotContain("sig=", variables);
            }
        }
        else
        {
            Assert.True(CodeOf(detail) == WorkflowErrorCode.InstanceAccessDenied, $"{name} detail {CodeOf(detail)} instance {instanceId}");
            Assert.True(CodeOf(history) == WorkflowErrorCode.InstanceAccessDenied, $"{name} history {CodeOf(history)} instance {instanceId}");
        }
    }

    private static async Task AssertFileHidden(HttpClient client, long fileId)
    {
        var download = await (await client.GetAsync($"/api/v1/sys/file/{fileId}/download")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.FileNotFound, download.GetProperty("code").GetInt32());
        Assert.DoesNotContain(fileId, Ids(await Get(client, "/api/v1/sys/file/page?Current=1&Size=50"), "id"));
    }

    private static async Task AssertFileBytes(HttpClient client, long fileId, byte[] expected)
    {
        var download = await client.GetAsync($"/api/v1/sys/file/{fileId}/download");
        Assert.True(download.IsSuccessStatusCode, await download.Content.ReadAsStringAsync());
        Assert.Equal(expected, await download.Content.ReadAsByteArrayAsync());
    }

    private static async Task<(long Id, byte[] Bytes, string ViewUrl)> UploadPng(HttpClient client)
    {
        var bytes = Encoding.UTF8.GetBytes("iso-png-" + Guid.NewGuid().ToString("N"));
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "iso.png");
        var data = (await (await client.PostAsync("/api/v1/sys/file/upload", form)).ReadEnvelope()).GetProperty("data");
        Assert.True(data.TryGetProperty("id", out _));
        return (data.GetProperty("id").GetInt64(), bytes, data.GetProperty("viewUrl").GetString()!);
    }

    private static async Task<long> CreateRole(IServiceProvider sp, string name, DataScopeType scope, params string[] permissions)
    {
        var role = new SysRole
        {
            Name = name,
            Code = name + "-" + Guid.NewGuid().ToString("N")[..6],
            Enabled = true,
        };
        await sp.GetRequiredService<IRepository<SysRole>>().InsertAsync(role);
        if (permissions.Length > 0)
        {
            var db = sp.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            var menuIds = await db.Queryable<SysMenu>()
                .Where(m => permissions.Contains(m.Permission))
                .Select(m => m.Id)
                .ToListAsync();
            Assert.Equal(permissions.Length, menuIds.Distinct().Count());
            await sp.GetRequiredService<IRbacService>().SetRoleMenusAsync(role.Id, menuIds);
        }
        await sp.GetRequiredService<IRbacService>().SetRoleDataScopeAsync(role.Id, scope);
        return role.Id;
    }

    private static async Task<long> AddUser(IServiceProvider sp, string account, long orgId, params long[] roleIds)
    {
        var created = await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = account,
            Password = Password,
            Name = account,
            Enabled = true,
            OrgId = orgId,
            RoleIds = roleIds,
        });
        return created.Id;
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var added = await Post(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, CodeOf(added));
        var id = added.GetProperty("data").GetInt64();
        var published = await Post(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, CodeOf(published));
        return id;
    }

    private static object MainModel(long ccUserId, long approverId) => new
    {
        version = 1,
        formSchema = new
        {
            version = 1,
            fields = new object[]
            {
                new { key = "note", label = "备注", type = "text", required = true },
                new { key = "file", label = "附件", type = "attachment", required = true, props = new { multiple = false } },
            },
        },
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "cc1",
                type = "cc",
                name = "抄送",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { ccUserId } },
                    },
                },
                next = new
                {
                    id = "approve-1",
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
                    next = (object?)null,
                },
            },
        },
    };

    private static object ApprovalModel(long approverId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "approve-1",
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
                next = (object?)null,
            },
        },
    };

    private static void AssertSame(IEnumerable<long> expected, IEnumerable<long> actual) =>
        Assert.Equal(expected.OrderBy(id => id), actual.OrderBy(id => id));

    private static HashSet<long> Ids(JsonElement envelope, string property) =>
        envelope.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty(property).GetInt64())
            .ToHashSet();

    private static int CodeOf(JsonElement envelope) => envelope.GetProperty("code").GetInt32();

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory factory, string account)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<JsonElement> Get(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> Post(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private static async Task<JsonElement> Put(HttpClient client, string path, object body) =>
        await (await client.PutJson(path, body)).ReadEnvelope();
}
