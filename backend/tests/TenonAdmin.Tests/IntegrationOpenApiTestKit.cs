extern alias integrationhost;

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Integration;
using TenonAdmin.Services;
using DemoTicket = integrationhost::TenonAdmin.IntegrationTestHost.DemoTicket;

namespace TenonAdmin.Tests;

/// <summary>开放接口测试的快捷方法:建应用 + 授权 + 绑定范围、带凭据的客户端、造机构与工单。</summary>
internal static class IntegrationOpenApiTestKit
{
    public const string OrgList = "GET:/api/open/v1/org-tickets";
    public const string OrgGet = "GET:/api/open/v1/org-tickets/{id}";
    public const string OrgCreate = "POST:/api/open/v1/org-tickets";
    public const string OrgUpdate = "PUT:/api/open/v1/org-tickets/{id}";
    public const string OrgDelete = "DELETE:/api/open/v1/org-tickets/{id}";
    public const string PartnerList = "GET:/api/open/v1/partner-tickets";
    public const string PartnerGet = "GET:/api/open/v1/partner-tickets/{id}";
    public const string PartnerCreate = "POST:/api/open/v1/partner-tickets";
    public const string PartnerClose = "POST:/api/open/v1/partner-tickets/{id}/close";
    public const string PartnerCallback = "POST:/api/open/v1/partner-callbacks/ticket-status";
    public const string PartnerLookup = "GET:/api/open/v1/partner-lookups/{key}";
    public const string Ping = "GET:/api/open/v1/ping";
    public const string Boom = "POST:/api/open/v1/ping/boom";

    public static readonly string[] AllOrgTicketGrants = [OrgList, OrgGet, OrgCreate, OrgUpdate, OrgDelete];

    public static OpenAppScopeBindingInput OrgScope(params long[] orgIds) =>
        new() { ScopeKey = OpenApiDataScopes.Org, Values = orgIds.Select(i => i.ToString()).ToList() };

    public static OpenAppScopeBindingInput PartnerScope(params string[] partners) =>
        new() { ScopeKey = "partner", Values = partners };

    public static OpenAppScopeBindingInput AllOf(string scopeKey) => new() { ScopeKey = scopeKey, AllValues = true };

    public static async Task<(long AppId, string ApiKey)> NewAppAsync(
        IServiceProvider services,
        string code,
        IReadOnlyList<string> grants,
        IReadOnlyList<OpenAppScopeBindingInput> scopes,
        long? ownerOrgId = null)
    {
        var (appId, issued) = await IntegrationTestSupport.CreateAppWithKeyAsync(services, code, ownerOrgId: ownerOrgId);
        using var scope = services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IOpenAppAuthorizationService>();
        await authorization.SetGrantsAsync(appId, new OpenAppGrantInput { Permissions = grants });
        await authorization.SetScopesAsync(appId, new OpenAppScopeInput { Bindings = scopes });
        return (appId, issued.ApiKey);
    }

    public static HttpClient OpenClient(this IntegrationAppFactory f, string apiKey)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Add(OpenAppAuthenticationDefaults.HeaderName, apiKey);
        return client;
    }

    public static async Task<long> NewOrgAsync(IServiceProvider services, string name, long parentId = 0)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOrgService>().AddAsync(new OrgInput
        {
            Name = name,
            Code = "itg-" + Guid.NewGuid().ToString("N")[..10],
            ParentId = parentId,
        });
    }

    public static async Task<long> SeedTicketAsync(IServiceProvider services, string title, string partner, long? orgId, string? internalNote = "内部备注")
    {
        using var scope = services.CreateScope();
        var ticket = new DemoTicket { Title = title, PartnerCode = partner, Amount = 10, CreateOrgId = orgId, InternalNote = internalNote };
        await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Insertable(ticket).ExecuteCommandAsync();
        return ticket.Id;
    }

    public static async Task<DemoTicket?> LoadTicketAsync(IServiceProvider services, long id)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>()
            .Queryable<DemoTicket>().ClearFilter().Where(t => t.Id == id).FirstAsync();
    }

    public static async Task<(int Status, JsonElement Body)> Send(this HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.ReadEnvelope());
    }

    public static int Code(this JsonElement envelope) => envelope.GetProperty("code").GetInt32();
}
