using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>发布校验上线前已经入库的空办理人/失效用户,发起时仍要拦住。</summary>
public class WfLegacyStartValidationTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Legacy_empty_approver_is_hidden_from_startable_and_cannot_start()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-legacy-starter", "旧流程发起人");
        var definitionId = await InsertPublished(f, "旧空审批", EmptyApproverModel(), starterId);
        var starter = await ClientFor(f, "wf-legacy-starter");

        var list = await (await starter.GetAsync("/api/v1/workflow/instance/startable")).ReadEnvelope();
        Assert.Equal(0, list.GetProperty("code").GetInt32());
        Assert.DoesNotContain(
            list.GetProperty("data").EnumerateArray(),
            row => row.GetProperty("id").GetInt64() == definitionId);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(WorkflowErrorCode.ModelInvalid, start.GetProperty("code").GetInt32());
        Assert.Equal("assigneeRequired", start.GetProperty("args").GetProperty("reason").GetString());

        var detail = await (await starter.GetAsync($"/api/v1/workflow/instance/startable/{definitionId}")).ReadEnvelope();
        Assert.Equal(WorkflowErrorCode.ModelInvalid, detail.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Legacy_disabled_assignee_cannot_start()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, "wf-legacy-off-starter", "失效办理发起人");
        var approverId = await AddUser(admin, "wf-legacy-off-user", "已停用办理人");
        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/sys/user/{approverId}/enabled", new { enabled = false })).ReadEnvelope()).GetProperty("code").GetInt32());
        var definitionId = await InsertPublished(f, "旧失效办理人", UserApproverModel(approverId), starterId);
        var starter = await ClientFor(f, "wf-legacy-off-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(WorkflowErrorCode.ModelInvalid, start.GetProperty("code").GetInt32());
        Assert.Equal("assigneeUserUnavailable", start.GetProperty("args").GetProperty("reason").GetString());
    }

    private static WfModel EmptyApproverModel() => new()
    {
        Version = 1,
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Name = "发起",
            Next = new WfNode
            {
                Id = "apm-empty",
                Type = WfNodeType.Approval,
                Name = "空审批",
                Props = new WfNodeProps { Nobody = WfNobodyAction.AutoPass },
            },
        },
    };

    private static WfModel UserApproverModel(long userId) => new()
    {
        Version = 1,
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Name = "发起",
            Next = new WfNode
            {
                Id = "approval",
                Type = WfNodeType.Approval,
                Name = "审批",
                Props = new WfNodeProps
                {
                    Assignee = new WfAssignee
                    {
                        Provider = ApproverProviderKeys.User,
                        Params = new Dictionary<string, JsonElement>
                        {
                            ["userIds"] = JsonSerializer.SerializeToElement(new[] { userId }),
                        },
                    },
                },
            },
        },
    };

    private static async Task<long> InsertPublished(WorkflowAppFactory f, string name, WfModel model, long userId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var definition = new WfDefinition
        {
            Name = name,
            Status = WfDefinitionStatus.Published,
            CurrentVersion = 1,
            CreateUserId = userId,
            CreateOrgId = 1,
        };
        await db.Insertable(definition).ExecuteCommandAsync();
        Assert.True(definition.Id > 0);
        await db.Insertable(new WfDefinitionVersion
        {
            DefinitionId = definition.Id,
            Version = 1,
            ModelJson = WfModelJson.Serialize(model),
            PublishTime = DateTime.UtcNow,
            PublishUserId = userId,
            CreateUserId = userId,
        }).ExecuteCommandAsync();
        return definition.Id;
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
