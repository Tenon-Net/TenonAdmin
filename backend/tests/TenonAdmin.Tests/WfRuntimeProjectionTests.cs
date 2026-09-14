using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>运行时公开投影的安全回归：客户端不能读执行凭据、内部参数或原始事件载荷。</summary>
public class WfRuntimeProjectionTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Runtime_model_and_history_are_allowlisted()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var definitionId = await Publish(admin, "运行时脱敏", SensitiveModel());

        var startable = await GetEnvelope(admin, $"/api/v1/workflow/instance/startable/{definitionId}");
        var startableJson = startable.GetProperty("data").GetProperty("model").GetRawText();
        Assert.DoesNotContain("webhookUrl", startableJson, StringComparison.Ordinal);
        Assert.DoesNotContain("webhookHeaders", startableJson, StringComparison.Ordinal);
        Assert.DoesNotContain("aiInstructions", startableJson, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-assignee-parameter", startableJson, StringComparison.Ordinal);

        var started = await PostEnvelope(admin, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            var token = await db.Queryable<WfToken>().Where(t => t.InstanceId == instanceId).FirstAsync();
            await db.Insertable(new WfHistory
            {
                InstanceId = instanceId,
                EventType = WfHistoryEventType.NodeEnter,
                NodeId = "approval",
                TokenId = token?.Id,
                PayloadJson = "{\"operator\":\"internal-user\",\"secret\":\"history-secret\"}",
                Sequence = 999,
            }).ExecuteCommandAsync();
        }

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        var detailJson = detail.GetProperty("data").GetRawText();
        Assert.DoesNotContain("webhookUrl", detailJson, StringComparison.Ordinal);
        Assert.DoesNotContain("webhookHeaders", detailJson, StringComparison.Ordinal);
        Assert.DoesNotContain("aiInstructions", detailJson, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-assignee-parameter", detailJson, StringComparison.Ordinal);

        var history = await GetEnvelope(admin, $"/api/v1/workflow/instance/history/{instanceId}");
        var historyItems = history.GetProperty("data").EnumerateArray().ToList();
        var injected = Assert.Single(historyItems, item => item.GetProperty("sequence").GetInt32() == 999);
        Assert.Equal(JsonValueKind.Null, injected.GetProperty("payloadJson").ValueKind);
        foreach (var item in history.GetProperty("data").EnumerateArray())
            Assert.DoesNotContain("history-secret", item.GetRawText(), StringComparison.Ordinal);

    }

    [Fact]
    public async Task Corrupt_form_values_fail_detail_projection_instead_of_becoming_empty()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin");
        var definitionId = await Publish(admin, "损坏变量", FormModel());
        var started = await PostEnvelope(admin, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            variablesJson = "{\"title\":\"ok\"}",
        });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            await db.Updateable<WfInstance>()
                .SetColumns(_ => new WfInstance { VariablesJson = "{broken" })
                .Where(instance => instance.Id == instanceId)
                .ExecuteCommandAsync();
        }

        var detail = await GetEnvelope(admin, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(WorkflowErrorCode.FormValueInvalid, detail.GetProperty("code").GetInt32());
    }

    private static object SensitiveModel() => new
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
                    assignee = new
                    {
                        provider = "initiator",
                        @params = new { value = "internal-assignee-parameter" },
                    },
                    returnPolicy = "prev",
                },
                next = new
                {
                    id = "webhook",
                    type = "webhook",
                    name = "回调",
                    props = new
                    {
                        webhookUrl = "https://internal.example/secret",
                        webhookHeaders = new Dictionary<string, string?>
                        {
                            ["Authorization"] = "Bearer webhook-secret",
                        },
                    },
                    next = new
                    {
                        id = "ai",
                        type = "aiDecision",
                        name = "AI 审查",
                        props = new
                        {
                            assignee = new { provider = "initiator", @params = new { } },
                            aiInstructions = "private-ai-instruction",
                            aiInputFields = new[] { "title" },
                        },
                    },
                },
            },
        },
    };

    private static object FormModel() => new
    {
        version = 1,
        formSchema = new
        {
            version = 1,
            fields = new[] { new { key = "title", label = "标题", type = "text", required = true } },
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
                props = new { assignee = new { provider = "initiator", @params = new { } } },
            },
        },
    };

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory factory, string account)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
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
        await (await client.PostAsJsonAsync(path, body)).ReadEnvelope();
}
