using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 11 抄送列表:独立列表 + 已读。抄送不是待办。
/// <para>变异:去掉 <c>PageMineAsync</c> 的 <c>UserId</c> 过滤 → 对方看见行;
/// 去掉 <c>MarkReadAsync</c> 的主人守卫 → 他人也能标;
/// 去掉 <c>GetAsync</c> 里的标已读 → 打开详情后仍未读;
/// 去掉 <c>MarkMyCcReadAsync</c> 的 <c>UserId</c> → 发起人打开详情后抄送人行被误标。</para>
/// </summary>
public class WfCcTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Page_mine_returns_only_current_users_cc()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cc-page-starter");
        var watcherA = await AddUser(admin, "wf-cc-page-a");
        var watcherB = await AddUser(admin, "wf-cc-page-b");
        var approverId = await AddUser(admin, "wf-cc-page-approver");
        var definitionId = await Publish(admin, "抄送-列表隔离",
            CcThenApprovalModel(watcherA, approverId));

        var starter = await ClientFor(f, "wf-cc-page-starter");
        var a = await ClientFor(f, "wf-cc-page-a");
        var b = await ClientFor(f, "wf-cc-page-b");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        var aPage = await GetEnvelope(a, "/api/v1/workflow/cc/page?Current=1&Size=20");
        Assert.Equal(0, aPage.GetProperty("code").GetInt32());
        var aRow = Assert.Single(aPage.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(instanceId, aRow.GetProperty("instanceId").GetInt64());
        Assert.False(aRow.GetProperty("isRead").GetBoolean());
        Assert.Equal("cc1", aRow.GetProperty("nodeId").GetString());

        var bPage = await GetEnvelope(b, "/api/v1/workflow/cc/page?Current=1&Size=20");
        Assert.Equal(0, bPage.GetProperty("code").GetInt32());
        Assert.Empty(bPage.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Mark_read_is_idempotent_for_owner()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cc-read-starter");
        var watcherId = await AddUser(admin, "wf-cc-read-w");
        var approverId = await AddUser(admin, "wf-cc-read-approver");
        var definitionId = await Publish(admin, "抄送-标已读幂等",
            CcThenApprovalModel(watcherId, approverId));

        var starter = await ClientFor(f, "wf-cc-read-starter");
        var watcher = await ClientFor(f, "wf-cc-read-w");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());

        var page = await GetEnvelope(watcher, "/api/v1/workflow/cc/page?Current=1&Size=20");
        var ccId = Assert.Single(page.GetProperty("data").GetProperty("items").EnumerateArray())
            .GetProperty("id").GetInt64();

        var first = await PostEnvelope(watcher, "/api/v1/workflow/cc/read", new { id = ccId });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        var second = await PostEnvelope(watcher, "/api/v1/workflow/cc/read", new { id = ccId });
        Assert.Equal(0, second.GetProperty("code").GetInt32());

        var after = await GetEnvelope(watcher, "/api/v1/workflow/cc/page?Current=1&Size=20");
        var row = Assert.Single(after.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.True(row.GetProperty("isRead").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("readTime").ValueKind);
    }

    [Fact]
    public async Task Mark_read_of_others_row_returns_48027()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cc-deny-starter");
        var watcherId = await AddUser(admin, "wf-cc-deny-w");
        await AddUser(admin, "wf-cc-deny-s");
        var approverId = await AddUser(admin, "wf-cc-deny-approver");
        var definitionId = await Publish(admin, "抄送-他人不能标",
            CcThenApprovalModel(watcherId, approverId));

        var starter = await ClientFor(f, "wf-cc-deny-starter");
        var watcher = await ClientFor(f, "wf-cc-deny-w");
        var stranger = await ClientFor(f, "wf-cc-deny-s");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());

        var page = await GetEnvelope(watcher, "/api/v1/workflow/cc/page?Current=1&Size=20");
        var ccId = Assert.Single(page.GetProperty("data").GetProperty("items").EnumerateArray())
            .GetProperty("id").GetInt64();

        var denied = await PostEnvelope(stranger, "/api/v1/workflow/cc/read", new { id = ccId });
        Assert.Equal(WorkflowErrorCode.CcNotFound, denied.GetProperty("code").GetInt32());

        var still = await GetEnvelope(watcher, "/api/v1/workflow/cc/page?Current=1&Size=20");
        Assert.False(Assert.Single(still.GetProperty("data").GetProperty("items").EnumerateArray())
            .GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task Opening_instance_detail_marks_cc_read()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cc-detail-starter");
        var watcherId = await AddUser(admin, "wf-cc-detail-w");
        var approverId = await AddUser(admin, "wf-cc-detail-approver");
        var definitionId = await Publish(admin, "抄送-详情即已读",
            CcThenApprovalModel(watcherId, approverId));

        var starter = await ClientFor(f, "wf-cc-detail-starter");
        var watcher = await ClientFor(f, "wf-cc-detail-w");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        var before = await GetEnvelope(watcher, "/api/v1/workflow/cc/page?Current=1&Size=20");
        Assert.False(Assert.Single(before.GetProperty("data").GetProperty("items").EnumerateArray())
            .GetProperty("isRead").GetBoolean());

        var detail = await GetEnvelope(watcher, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());

        var after = await GetEnvelope(watcher, "/api/v1/workflow/cc/page?Current=1&Size=20");
        var row = Assert.Single(after.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.True(row.GetProperty("isRead").GetBoolean());
    }

    /// <summary>
    /// 发起人打开详情不得把抄送人的未读行翻掉。
    /// 变异:去掉 <c>MarkMyCcReadAsync</c> 的 <c>UserId ==</c> → 本实例未读全被标 → 本条红。
    /// </summary>
    [Fact]
    public async Task Starter_opening_detail_does_not_mark_others_cc()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cc-starter-view-starter");
        var watcherId = await AddUser(admin, "wf-cc-starter-view-w");
        var approverId = await AddUser(admin, "wf-cc-starter-view-approver");
        var definitionId = await Publish(admin, "抄送-发起人打开不误标",
            CcThenApprovalModel(watcherId, approverId));

        var starter = await ClientFor(f, "wf-cc-starter-view-starter");
        var watcher = await ClientFor(f, "wf-cc-starter-view-w");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());

        var page = await GetEnvelope(watcher, "/api/v1/workflow/cc/page?Current=1&Size=20");
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var row = Assert.Single(page.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(instanceId, row.GetProperty("instanceId").GetInt64());
        Assert.False(row.GetProperty("isRead").GetBoolean());
    }

    private static object CcThenApprovalModel(long ccUserId, long approverId) => new
    {
        version = 1,
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

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
        };
        var env = await PostEnvelope(admin, "/api/v1/sys/user", body);
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

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
