using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>同一数据范围内，未删除的流程名称不能重复。删掉之后可以再用。</summary>
public class WfDefinitionNameTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Definition_name_is_unique_among_active_rows()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        var first = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name = "费用报销" });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        var firstId = first.GetProperty("data").GetInt64();

        var duplicate = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name = "  费用报销  " });
        Assert.Equal(WorkflowErrorCode.DefinitionNameExists, duplicate.GetProperty("code").GetInt32());

        var second = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name = "请假申请" });
        Assert.Equal(0, second.GetProperty("code").GetInt32());
        var secondId = second.GetProperty("data").GetInt64();

        var renamed = await PostEnvelope(admin, "/api/v1/workflow/definition/update", new { id = secondId, name = "费用报销" });
        Assert.Equal(WorkflowErrorCode.DefinitionNameExists, renamed.GetProperty("code").GetInt32());

        var kept = await PostEnvelope(admin, "/api/v1/workflow/definition/update", new { id = firstId, name = "费用报销" });
        Assert.Equal(0, kept.GetProperty("code").GetInt32());

        var deleted = await (await admin.DeleteAsync($"/api/v1/workflow/definition/{firstId}")).ReadEnvelope();
        Assert.Equal(0, deleted.GetProperty("code").GetInt32());

        var reused = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name = "费用报销" });
        Assert.Equal(0, reused.GetProperty("code").GetInt32());
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
