using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 普通办理人用工作流选人接口,不依赖用户管理权限;
/// 转办、委托、加签不能把单交回发起人。
/// </summary>
public class WfParticipantLookupTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Approver_can_look_up_enabled_users_without_user_admin_permission()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pick-actor", "选人办理人");
        var targetId = await AddUser(admin, "wf-pick-target", "选人目标");
        var disabledId = await AddUser(admin, "wf-pick-disabled", "已停用");
        var disabled = await admin.PutJson($"/api/v1/sys/user/{disabledId}/enabled", new { enabled = false });
        Assert.Equal(0, (await disabled.ReadEnvelope()).GetProperty("code").GetInt32());

        var actor = await ClientFor(f, "wf-pick-actor");
        var userPage = await actor.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");
        Assert.Equal(HttpStatusCode.Forbidden, userPage.StatusCode);

        var page = await (await actor.GetAsync(
            "/api/v1/workflow/participant/page?Current=1&Size=20&Keyword=wf-pick")).ReadEnvelope();
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetInt64() == targetId
                                        && item.GetProperty("name").GetString() == "选人目标"
                                        && item.GetProperty("account").GetString() == "wf-pick-target");
        Assert.DoesNotContain(items, item => item.GetProperty("id").GetInt64() == disabledId);
        Assert.All(items, item =>
        {
            Assert.False(item.TryGetProperty("phone", out _));
            Assert.False(item.TryGetProperty("email", out _));
            Assert.False(item.TryGetProperty("password", out _));
        });
    }

    [Fact]
    public async Task Participant_page_hides_builtin_data_scope_fixture_accounts()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var realId = await AddUser(admin, "wf-real-actor", "真实办理人");
        var actor = await ClientFor(f, "wf-real-actor");

        foreach (var keyword in new[] { "全部数据", "本机构数据", "本机构及以下", "仅本人数据", "自定义范围" })
        {
            var scoped = await (await actor.GetAsync(
                $"/api/v1/workflow/participant/page?Current=1&Size=20&Keyword={Uri.EscapeDataString(keyword)}")).ReadEnvelope();
            Assert.Equal(0, scoped.GetProperty("code").GetInt32());
            Assert.Empty(scoped.GetProperty("data").GetProperty("items").EnumerateArray());
        }

        var real = await (await actor.GetAsync(
            "/api/v1/workflow/participant/page?Current=1&Size=20&Keyword=wf-real")).ReadEnvelope();
        Assert.Equal(0, real.GetProperty("code").GetInt32());
        var items = real.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetInt64() == realId
                                        && item.GetProperty("account").GetString() == "wf-real-actor");
    }

    [Fact]
    public async Task Transfer_delegate_and_add_sign_reject_the_starter()
    {
        using var f = new WorkflowAppFactory();
        await GrantSignPermissions(f);
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-self-starter", "发起人");
        var approverId = await AddUser(admin, "wf-self-approver", "办理人");
        var definitionId = await Publish(admin, "不能转回发起人", SingleModel(approverId));
        var starter = await ClientFor(f, "wf-self-starter");
        var approver = await ClientFor(f, "wf-self-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var transferred = await PostEnvelope(approver, "/api/v1/workflow/task/transfer", new { taskId, toUserId = starterId });
        Assert.Equal(WorkflowErrorCode.TransferTargetInvalid, transferred.GetProperty("code").GetInt32());
        Assert.Equal("starterNotAllowed", transferred.GetProperty("args").GetProperty("reason").GetString());

        var delegated = await PostEnvelope(approver, "/api/v1/workflow/task/delegate", new { taskId, toUserId = starterId });
        Assert.Equal(WorkflowErrorCode.DelegateTargetInvalid, delegated.GetProperty("code").GetInt32());
        Assert.Equal("starterNotAllowed", delegated.GetProperty("args").GetProperty("reason").GetString());

        var added = await PostEnvelope(approver, "/api/v1/workflow/task/add-sign", new { taskId, toUserId = starterId, requestId = "wf-self-add" });
        Assert.Equal(WorkflowErrorCode.SignTargetInvalid, added.GetProperty("code").GetInt32());
        Assert.Equal("starterNotAllowed", added.GetProperty("args").GetProperty("reason").GetString());

        var todo = await (await starter.GetAsync("/api/v1/workflow/task/todo?Current=1&Size=20")).ReadEnvelope();
        Assert.Equal(0, todo.GetProperty("code").GetInt32());
        Assert.Empty(todo.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    private static object SingleModel(long userId) => new
    {
        version = 1,
        root = new
        {
            id = "start", type = "start", name = "发起",
            next = new
            {
                id = "approval", type = "approval", name = "审批",
                props = new
                {
                    assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } } },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

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

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task GrantSignPermissions(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var menuIds = await db.Queryable<TenonAdmin.Services.SysMenu>()
            .Where(m => m.Permission == "POST:/api/v1/workflow/task/add-sign"
                        || m.Permission == "POST:/api/v1/workflow/task/remove-sign")
            .Select(m => m.Id)
            .ToListAsync();
        foreach (var menuId in menuIds)
            await db.Insertable(new TenonAdmin.Services.SysRoleMenu { RoleId = 2, MenuId = menuId }).ExecuteCommandAsync();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
