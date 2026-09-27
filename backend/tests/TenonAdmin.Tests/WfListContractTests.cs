using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 10 列表契约:「我发起的」与「我已办的」今天已有 HTTP 信封,但没有独立钉子。
/// Task 12 是前端页,后端必须先保证只返回当前用户的行。
/// <para>变异:去掉 <c>PageMineAsync</c> 的 <c>StarterUserId</c> 过滤或 <c>PageDoneAsync</c> 的
/// <c>UserId</c> 过滤 → 对方看见不属于自己的行 → 红。</para>
/// <para><b>机构必须相同</b>:<c>wf_instance</c> 是 <c>IOrgScoped</c>。造用户时 <c>orgId</c> 与发起人
/// 一致(本文件一律 1),否则 page 空可能是数据范围过滤而不是 userId 过滤,变异钉不住。</para>
/// </summary>
public class WfListContractTests
{
    private const string Password = "Test@123456";

    /// <summary>
    /// B 调 <c>GET /api/v1/workflow/instance/page</c> 只看到自己发起的在途单;A 办完的那单不得漏过来。
    /// </summary>
    [Fact]
    public async Task Page_mine_returns_only_current_users_instances()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-list-mine-a");
        await AddUser(admin, "wf-list-mine-b");
        var approverAId = await AddUser(admin, "wf-list-mine-approver-a");
        var approverBId = await AddUser(admin, "wf-list-mine-approver-b");
        var definitionId = await Publish(admin, "列表-我发起的", AnyApprovalModel(approverAId, approverBId));

        var a = await ClientFor(f, "wf-list-mine-a");
        var b = await ClientFor(f, "wf-list-mine-b");
        var approverA = await ClientFor(f, "wf-list-mine-approver-a");

        var startedA = await PostEnvelope(a, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, startedA.GetProperty("code").GetInt32());
        var aInstanceId = startedA.GetProperty("data").GetProperty("instanceId").GetInt64();
        var aTaskId = startedA.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approved = await PostEnvelope(approverA, "/api/v1/workflow/task/approve", new { taskId = aTaskId });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());

        var startedB = await PostEnvelope(b, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, startedB.GetProperty("code").GetInt32());
        var bInstanceId = startedB.GetProperty("data").GetProperty("instanceId").GetInt64();

        var page = await GetEnvelope(b, "/api/v1/workflow/instance/page?Current=1&Size=20");
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var ids = page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt64())
            .ToList();
        Assert.Equal([bInstanceId], ids);
        Assert.DoesNotContain(aInstanceId, ids);
    }

    /// <summary>
    /// A 办完后 <c>GET /api/v1/workflow/task/done</c> 有且仅有 A 的已办;B(在途发起人)调同一接口为空。
    /// </summary>
    [Fact]
    public async Task Page_done_returns_only_current_users_his_tasks()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-list-done-starter-a");
        await AddUser(admin, "wf-list-done-b");
        var aId = await AddUser(admin, "wf-list-done-a");
        var hangingId = await AddUser(admin, "wf-list-done-hanging");
        var definitionId = await Publish(admin, "列表-我已办的", AnyApprovalModel(aId, hangingId));

        var starterA = await ClientFor(f, "wf-list-done-starter-a");
        var a = await ClientFor(f, "wf-list-done-a");
        var b = await ClientFor(f, "wf-list-done-b");

        var startedA = await PostEnvelope(starterA, "/api/v1/workflow/instance/start",
            new { definitionId });
        Assert.Equal(0, startedA.GetProperty("code").GetInt32());
        var aInstanceId = startedA.GetProperty("data").GetProperty("instanceId").GetInt64();
        var aTaskId = startedA.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approved = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = aTaskId });
        Assert.Equal(0, approved.GetProperty("code").GetInt32());

        var startedB = await PostEnvelope(b, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, startedB.GetProperty("code").GetInt32());

        var aDone = await GetEnvelope(a, "/api/v1/workflow/task/done?Current=1&Size=20");
        Assert.Equal(0, aDone.GetProperty("code").GetInt32());
        var aItems = aDone.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var aRow = Assert.Single(aItems);
        Assert.Equal(aInstanceId, aRow.GetProperty("instanceId").GetInt64());
        Assert.Equal((int)WfTaskAction.Approve, aRow.GetProperty("action").GetInt32());

        var bDone = await GetEnvelope(b, "/api/v1/workflow/task/done?Current=1&Size=20");
        Assert.Equal(0, bDone.GetProperty("code").GetInt32());
        Assert.Empty(bDone.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    private static object AnyApprovalModel(params long[] userIds) => new
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
                        @params = new Dictionary<string, object> { ["userIds"] = userIds },
                    },
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

    /// <summary>与发起人同一机构(<c>orgId = 1</c>),避免 page 空被数据范围过滤误解释。</summary>
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
