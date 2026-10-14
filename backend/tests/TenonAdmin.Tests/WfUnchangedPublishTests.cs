using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>内容没变时再次发布不生成新版本。已发布直接拒绝；停用则只恢复为已发布。</summary>
public class WfUnchangedPublishTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Republish_without_model_changes_does_not_create_a_version()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var approverId = await AddUser(admin, "wf-unchanged-approver", "未变发布办理人");
        var model = ApprovalModel(approverId, "审批");

        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name = "未变发布", model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();

        var first = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        Assert.Equal(1, first.GetProperty("data").GetInt32());

        var resaved = await PostEnvelope(admin, "/api/v1/workflow/definition/update", new { id, name = "未变发布", model });
        Assert.Equal(0, resaved.GetProperty("code").GetInt32());
        var unchanged = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(WorkflowErrorCode.DefinitionUnchanged, unchanged.GetProperty("code").GetInt32());
        Assert.Equal(1, await VersionCount(admin, id));
        Assert.Equal(1, await CurrentVersion(admin, id));

        var changed = await PostEnvelope(admin, "/api/v1/workflow/definition/update", new
        {
            id,
            name = "未变发布",
            model = ApprovalModel(approverId, "复核"),
        });
        Assert.Equal(0, changed.GetProperty("code").GetInt32());
        var second = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, second.GetProperty("code").GetInt32());
        Assert.Equal(2, second.GetProperty("data").GetInt32());

        var disabled = await PostEnvelope(admin, "/api/v1/workflow/definition/disable", new { id });
        Assert.Equal(0, disabled.GetProperty("code").GetInt32());
        Assert.DoesNotContain(id, await StartableIds(admin));

        var restored = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, restored.GetProperty("code").GetInt32());
        Assert.Equal(2, restored.GetProperty("data").GetInt32());
        Assert.Equal(2, await VersionCount(admin, id));
        Assert.Equal(2, await CurrentVersion(admin, id));
        Assert.Contains(id, await StartableIds(admin));
    }

    private static object ApprovalModel(long approverId, string approvalName) => new
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
                name = approvalName,
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { approverId } },
                    },
                    mode = "any",
                },
            },
        },
    };

    private static async Task<int> VersionCount(HttpClient admin, long id)
    {
        var versions = await (await admin.GetAsync($"/api/v1/workflow/definition/versions/{id}")).ReadEnvelope();
        Assert.Equal(0, versions.GetProperty("code").GetInt32());
        return versions.GetProperty("data").GetArrayLength();
    }

    private static async Task<int> CurrentVersion(HttpClient admin, long id)
    {
        var detail = await (await admin.GetAsync($"/api/v1/workflow/definition/{id}")).ReadEnvelope();
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        return detail.GetProperty("data").GetProperty("currentVersion").GetInt32();
    }

    private static async Task<List<long>> StartableIds(HttpClient admin)
    {
        var list = await (await admin.GetAsync("/api/v1/workflow/instance/startable")).ReadEnvelope();
        Assert.Equal(0, list.GetProperty("code").GetInt32());
        return list.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("id").GetInt64()).ToList();
    }

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

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
