extern alias integrationhost;

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.Services;
using static TenonAdmin.Tests.IntegrationOpenApiTestKit;
using static TenonAdmin.Tests.IntegrationTestSupport;

namespace TenonAdmin.Tests;

/// <summary>
/// G03:开放接口独立认证、默认拒绝授权与业务数据隔离。应用 A 调不了仅授予 B 的端点、读写不了范围外记录;
/// 未绑定范围拒绝(不退化为全量);应用凭据进不了后台接口,用户令牌进不了开放接口;调用记录真实应用身份且不混入用户操作日志。
/// </summary>
public class IntegrationOpenApiAuthorizationTests
{
    [Fact]
    public async Task App_only_reaches_granted_endpoints_and_grant_changes_apply_immediately()
    {
        using var f = new IntegrationAppFactory();
        var org = await NewOrgAsync(f.Services, "一号机构");
        var (appA, keyA) = await NewAppAsync(f.Services, "app-a", [Ping, OrgList], [OrgScope(org)]);
        var (_, keyB) = await NewAppAsync(f.Services, "app-b", [PartnerList, PartnerClose], [PartnerScope("P1")]);
        var ticket = await SeedTicketAsync(f.Services, "P1 工单", "P1", org);
        var a = f.OpenClient(keyA);
        var b = f.OpenClient(keyB);

        var (status, ping) = await a.Send(HttpMethod.Get, "/api/open/v1/ping");
        Assert.Equal(200, status);
        Assert.Equal("app-a", ping.GetProperty("data").GetProperty("appCode").GetString());
        Assert.Equal(OpenApiDataScopes.None, ping.GetProperty("data").GetProperty("scopeKey").GetString());

        // B 专属端点:A 被默认拒绝,B 可调用
        var (aClose, aCloseBody) = await a.Send(HttpMethod.Post, $"/api/open/v1/partner-tickets/{ticket}/close");
        Assert.Equal(403, aClose);
        Assert.Equal(IntegrationErrorCode.PermissionDenied, aCloseBody.Code());
        var (bClose, bCloseBody) = await b.Send(HttpMethod.Post, $"/api/open/v1/partner-tickets/{ticket}/close");
        Assert.Equal(200, bClose);
        Assert.Equal(0, bCloseBody.Code());
        Assert.True((await LoadTicketAsync(f.Services, ticket))!.Closed);

        // B 未被授予 ping
        Assert.Equal(IntegrationErrorCode.PermissionDenied, (await b.Send(HttpMethod.Get, "/api/open/v1/ping")).Body.Code());

        // 授权变更下一次请求即生效:授予 A 合作方列表后仍因未绑定合作方范围被拒,绑定后才放行
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IOpenAppAuthorizationService>()
                .SetGrantsAsync(appA, new OpenAppGrantInput { Permissions = [Ping, OrgList, PartnerList] });
        var (unbound, unboundBody) = await a.Send(HttpMethod.Get, "/api/open/v1/partner-tickets");
        Assert.Equal(403, unbound);
        Assert.Equal(IntegrationErrorCode.ScopeNotBound, unboundBody.Code());

        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IOpenAppAuthorizationService>()
                .SetScopesAsync(appA, new OpenAppScopeInput { Bindings = [OrgScope(org), PartnerScope("P1")] });
        Assert.Equal(0, (await a.Send(HttpMethod.Get, "/api/open/v1/partner-tickets")).Body.Code());

        // 撤销授权同样即时生效
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IOpenAppAuthorizationService>()
                .SetGrantsAsync(appA, new OpenAppGrantInput { Permissions = [OrgList] });
        Assert.Equal(IntegrationErrorCode.PermissionDenied, (await a.Send(HttpMethod.Get, "/api/open/v1/ping")).Body.Code());
    }

    [Fact]
    public async Task Org_scope_restricts_list_detail_update_delete_and_anchors_inserts_to_owner_org()
    {
        using var f = new IntegrationAppFactory();
        var org1 = await NewOrgAsync(f.Services, "华东");
        var org1Child = await NewOrgAsync(f.Services, "华东-上海", org1);
        var org2 = await NewOrgAsync(f.Services, "华南");
        var t1 = await SeedTicketAsync(f.Services, "华东单", "P1", org1);
        var t2 = await SeedTicketAsync(f.Services, "上海单", "P1", org1Child);
        var t3 = await SeedTicketAsync(f.Services, "华南单", "P2", org2);
        var (_, key) = await NewAppAsync(f.Services, "east-erp", AllOrgTicketGrants, [OrgScope(org1)], ownerOrgId: org1);
        var client = f.OpenClient(key);

        var (_, list) = await client.Send(HttpMethod.Get, "/api/open/v1/org-tickets?size=50");
        var ids = list.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToHashSet();
        Assert.Equal(new HashSet<long> { t1, t2 }, ids);                  // 绑定机构含下级,不含他机构
        Assert.DoesNotContain("internalNote", list.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createOrgId", list.GetRawText(), StringComparison.OrdinalIgnoreCase);

        // 范围外:按 Id 读、改、删都拒绝,库里不变
        Assert.Equal(60001, (await client.Send(HttpMethod.Get, $"/api/open/v1/org-tickets/{t3}")).Body.Code());
        Assert.Equal(60001, (await client.Send(HttpMethod.Put, $"/api/open/v1/org-tickets/{t3}", new { title = "篡改", partnerCode = "P2", amount = 1 })).Body.Code());
        Assert.Equal(60001, (await client.Send(HttpMethod.Delete, $"/api/open/v1/org-tickets/{t3}")).Body.Code());
        var untouched = await LoadTicketAsync(f.Services, t3);
        Assert.Equal("华南单", untouched!.Title);
        Assert.False(untouched.IsDelete);

        // 范围内:可改可删
        Assert.Equal(0, (await client.Send(HttpMethod.Put, $"/api/open/v1/org-tickets/{t1}", new { title = "已改", partnerCode = "P1", amount = 20 })).Body.Code());
        Assert.Equal("已改", (await LoadTicketAsync(f.Services, t1))!.Title);
        Assert.Equal(0, (await client.Send(HttpMethod.Delete, $"/api/open/v1/org-tickets/{t2}")).Body.Code());

        // 新增:审计列不伪造用户,数据范围锚点落到应用归属机构,且应用自己能读回
        var (_, created) = await client.Send(HttpMethod.Post, "/api/open/v1/org-tickets", new { title = "新单", partnerCode = "P1", amount = 5 });
        var newId = created.GetProperty("data").GetInt64();
        var row = await LoadTicketAsync(f.Services, newId);
        Assert.Null(row!.CreateUserId);
        Assert.Equal(org1, row.CreateOrgId);
        Assert.NotEqual(default, row.CreateTime);
        Assert.Equal(0, (await client.Send(HttpMethod.Get, $"/api/open/v1/org-tickets/{newId}")).Body.Code());

        // 应用改写的行不冒充用户:UpdateUserId 保持空
        Assert.Equal(0, (await client.Send(HttpMethod.Put, $"/api/open/v1/org-tickets/{newId}", new { title = "新单2", partnerCode = "P1", amount = 6 })).Body.Code());
        Assert.Null((await LoadTicketAsync(f.Services, newId))!.UpdateUserId);

        // 管理员显式授予全量后才看得到全部
        var (_, allKey) = await NewAppAsync(f.Services, "hq-erp", [OrgList], [AllOf(OpenApiDataScopes.Org)]);
        var (_, allList) = await f.OpenClient(allKey).Send(HttpMethod.Get, "/api/open/v1/org-tickets?size=50");
        Assert.Equal(3, allList.GetProperty("data").GetProperty("total").GetInt32());   // t1、t3、新单(t2 已删)
    }

    [Fact]
    public async Task Custom_partner_scope_filters_queries_and_rejects_out_of_scope_writes()
    {
        using var f = new IntegrationAppFactory();
        var org = await NewOrgAsync(f.Services, "任意机构");
        var p1 = await SeedTicketAsync(f.Services, "合作方一", "P1", org);
        var p2 = await SeedTicketAsync(f.Services, "合作方二", "P2", org);
        var (_, key) = await NewAppAsync(f.Services, "partner-p1", [PartnerList, PartnerGet, PartnerCreate], [PartnerScope("P1")]);
        var client = f.OpenClient(key);

        var (_, list) = await client.Send(HttpMethod.Get, "/api/open/v1/partner-tickets");
        Assert.Equal(new[] { p1 }, list.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()));
        Assert.Equal(60001, (await client.Send(HttpMethod.Get, $"/api/open/v1/partner-tickets/{p2}")).Body.Code());
        Assert.Equal(0, (await client.Send(HttpMethod.Get, $"/api/open/v1/partner-tickets/{p1}")).Body.Code());

        var (_, rejected) = await client.Send(HttpMethod.Post, "/api/open/v1/partner-tickets", new { title = "越权", partnerCode = "P2", amount = 1 });
        Assert.Equal(IntegrationErrorCode.DataOutOfScope, rejected.Code());
        var (_, accepted) = await client.Send(HttpMethod.Post, "/api/open/v1/partner-tickets", new { title = "本方", partnerCode = "P1", amount = 1 });
        Assert.Equal(0, accepted.Code());

        var (_, allKey) = await NewAppAsync(f.Services, "partner-all", [PartnerList], [AllOf("partner")]);
        var (_, all) = await f.OpenClient(allKey).Send(HttpMethod.Get, "/api/open/v1/partner-tickets");
        Assert.Equal(3, all.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Missing_scope_binding_or_policy_is_rejected_and_never_widened()
    {
        using var f = new IntegrationAppFactory();
        var org = await NewOrgAsync(f.Services, "机构");
        await SeedTicketAsync(f.Services, "一张单", "P1", org);
        var (appId, key) = await NewAppAsync(f.Services, "no-scope", [OrgList, PartnerList, Ping], []);
        var client = f.OpenClient(key);

        foreach (var path in new[] { "/api/open/v1/org-tickets", "/api/open/v1/partner-tickets" })
        {
            var (status, body) = await client.Send(HttpMethod.Get, path);
            Assert.Equal(403, status);
            Assert.Equal(IntegrationErrorCode.ScopeNotBound, body.Code());
        }
        Assert.Equal(0, (await client.Send(HttpMethod.Get, "/api/open/v1/ping")).Body.Code());   // none 范围无需绑定

        using var scope = f.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IOpenAppAuthorizationService>();
        await ExpectCodeAsync(IntegrationErrorCode.ScopeValueInvalid, () => authorization.SetScopesAsync(appId,
            new OpenAppScopeInput { Bindings = [new OpenAppScopeBindingInput { ScopeKey = "partner", Values = [] }] }));
        await ExpectCodeAsync(IntegrationErrorCode.ScopeValueInvalid, () => authorization.SetScopesAsync(appId,
            new OpenAppScopeInput { Bindings = [OrgScope(987654321)] }));
        await ExpectCodeAsync(IntegrationErrorCode.ScopePolicyUnknown, () => authorization.SetScopesAsync(appId,
            new OpenAppScopeInput { Bindings = [PartnerScope("x") with { ScopeKey = "warehouse" }] }));
        await ExpectCodeAsync(IntegrationErrorCode.ScopePolicyUnknown, () => authorization.SetScopesAsync(appId,
            new OpenAppScopeInput { Bindings = [AllOf(OpenApiDataScopes.None)] }));
        await ExpectCodeAsync(IntegrationErrorCode.ScopeValueInvalid, () => authorization.SetScopesAsync(appId,
            new OpenAppScopeInput { Bindings = [PartnerScope("a"), PartnerScope("b")] }));
        await ExpectCodeAsync(IntegrationErrorCode.GrantPermissionUnknown, () => authorization.SetGrantsAsync(appId,
            new OpenAppGrantInput { Permissions = ["GET:/api/v1/sys/user/page"] }));
        Assert.Empty((await authorization.GetScopesAsync(appId)));    // 失败的修改不留半截
    }

    [Fact]
    public async Task App_credentials_cannot_reach_admin_routes_and_user_tokens_cannot_reach_open_routes()
    {
        using var f = new IntegrationAppFactory();
        var (_, key) = await NewAppAsync(f.Services, "cross", [Ping], []);
        var admin = await AuthProbe.SuperAdmin(f);
        var adminToken = admin.DefaultRequestHeaders.Authorization!.Parameter!;

        // 应用凭据 → 后台接口:无论放在专用头还是 Bearer,都是未认证
        var keyOnly = f.OpenClient(key);
        await AuthProbe.AssertUnauthorized(await keyOnly.GetAsync("/api/v1/integration/app/page"));
        await AuthProbe.AssertUnauthorized(await keyOnly.GetAsync("/api/v1/sys/user/page"));
        var keyAsBearer = f.CreateClient();
        keyAsBearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        await AuthProbe.AssertUnauthorized(await keyAsBearer.GetAsync("/api/v1/integration/app/page"));

        // 用户令牌(即便是超管)→ 开放接口:未认证
        var (userStatus, userBody) = await admin.Send(HttpMethod.Get, "/api/open/v1/ping");
        Assert.Equal(401, userStatus);
        Assert.Equal(IntegrationErrorCode.CredentialInvalid, userBody.Code());

        // 两者同时出示:各通道只认自己的凭据
        var both = f.OpenClient(key);
        both.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal("cross", (await both.Send(HttpMethod.Get, "/api/open/v1/ping")).Body.GetProperty("data").GetProperty("appCode").GetString());
        Assert.Equal(0, (await both.Send(HttpMethod.Get, "/api/v1/integration/app/page")).Body.Code());

        // 缺失 / 无效 / 多值凭据:统一 401,附 WWW-Authenticate
        using var missing = await f.CreateClient().GetAsync("/api/open/v1/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Contains("X-Api-Key", missing.Headers.WwwAuthenticate.ToString());
        Assert.Equal(IntegrationErrorCode.CredentialInvalid, (await missing.ReadEnvelope()).Code());
        Assert.Equal(401, (await f.OpenClient("tna_not-a-key").Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
        var twoKeys = f.CreateClient();
        twoKeys.DefaultRequestHeaders.Add(OpenAppAuthenticationDefaults.HeaderName, [key, key]);
        Assert.Equal(401, (await twoKeys.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
    }

    [Fact]
    public async Task Open_calls_are_recorded_with_app_identity_and_kept_out_of_user_operation_log()
    {
        using var f = new IntegrationAppFactory();
        var org = await NewOrgAsync(f.Services, "机构");
        var (appId, key) = await NewAppAsync(f.Services, "audited", [OrgCreate, Boom], [OrgScope(org)], ownerOrgId: org);
        var client = f.OpenClient(key);
        client.DefaultRequestHeaders.Add(OpenApiHeaders.RequestId, "req-001");

        using var created = await client.PostJson("/api/open/v1/org-tickets", new { title = "记录我", partnerCode = "P1", amount = 1 });
        var traceId = created.Headers.GetValues(OpenApiHeaders.TraceId).Single();
        Assert.Equal("req-001", created.Headers.GetValues(OpenApiHeaders.RequestId).Single());
        Assert.Equal(0, (await created.ReadEnvelope()).Code());

        Assert.Equal(403, (await client.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);           // 未授予
        using var boom = await client.PostAsync("/api/open/v1/ping/boom", null);
        Assert.Equal(HttpStatusCode.InternalServerError, boom.StatusCode);
        Assert.Equal(401, (await f.OpenClient(OpenAppApiKey.Format(KeyIdOf(key), new OpenAppKeyGenerator().Generate().Secret))
            .Send(HttpMethod.Get, "/api/open/v1/ping")).Status);                                           // 秘密错误:凭据可识别
        Assert.Equal(401, (await f.OpenClient(OpenAppApiKey.Format("0123456789abcdef", new OpenAppKeyGenerator().Generate().Secret))
            .Send(HttpMethod.Get, "/api/open/v1/ping")).Status);                                           // 标识不存在:不落库

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var logs = await db.Queryable<IntegrationInboundLog>().OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(4, logs.Count);

        var ok = logs[0];
        Assert.Equal(appId, ok.AppId);
        Assert.Equal("audited", ok.AppCode);
        Assert.Equal(KeyIdOf(key), ok.KeyId);
        Assert.Equal(OrgCreate, ok.Route);
        Assert.Equal("/api/open/v1/org-tickets", ok.Path);
        Assert.Equal(InboundCallOutcome.Succeeded, ok.Outcome);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal(traceId, ok.TraceId);
        Assert.Equal("req-001", ok.ClientRequestId);
        Assert.Null(ok.CreateUserId);

        Assert.Equal((InboundCallOutcome.Forbidden, "not_granted", 403), (logs[1].Outcome, logs[1].FailureReason, logs[1].StatusCode));
        Assert.Equal((InboundCallOutcome.Error, "exception:InvalidOperationException", 500), (logs[2].Outcome, logs[2].FailureReason, logs[2].StatusCode));
        Assert.Equal((InboundCallOutcome.Unauthorized, "credential_mismatch", appId), (logs[3].Outcome, logs[3].FailureReason, logs[3].AppId));

        // 开放写调用不进用户操作日志;管理员的授权变更进
        var opLogs = await db.Queryable<SysOpLog>().ToListAsync();
        Assert.DoesNotContain(opLogs, l => l.Path.StartsWith("/api/open/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_authorization_changes_are_audited_and_catalog_lists_declared_endpoints()
    {
        using var f = new IntegrationAppFactory();
        var admin = await AuthProbe.SuperAdmin(f);
        var appId = (await (await admin.PostJson("/api/v1/integration/app", new { code = "catalog-app", name = "目录" })).ReadEnvelope())
            .GetProperty("data").GetInt64();

        var catalog = await (await admin.GetAsync("/api/v1/integration/catalog/endpoints")).ReadEnvelope();
        var endpoints = catalog.GetProperty("data").EnumerateArray().ToList();
        var close = endpoints.Single(e => e.GetProperty("permission").GetString() == PartnerClose);
        Assert.Equal("partner", close.GetProperty("scopeKey").GetString());
        Assert.Equal("v1", close.GetProperty("version").GetString());
        Assert.Equal("关闭合作方工单", close.GetProperty("summary").GetString());
        Assert.Contains(endpoints, e => e.GetProperty("permission").GetString() == Ping
                                        && e.GetProperty("scopeKey").GetString() == OpenApiDataScopes.None);
        Assert.DoesNotContain(endpoints, e => e.GetProperty("route").GetString()!.StartsWith("api/v1/", StringComparison.Ordinal));

        var scopes = await (await admin.GetAsync("/api/v1/integration/catalog/scopes")).ReadEnvelope();
        Assert.Equal(["org", "partner"], scopes.GetProperty("data").EnumerateArray().Select(s => s.GetProperty("key").GetString()!).ToArray());
        var orgId = await NewOrgAsync(f.Services, "可选机构");
        var options = await (await admin.GetAsync("/api/v1/integration/catalog/scopes/org/options?keyword=可选")).ReadEnvelope();
        Assert.Contains(options.GetProperty("data").EnumerateArray(), o => o.GetProperty("value").GetString() == orgId.ToString());

        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/integration/app/{appId}/grants", new { permissions = new[] { Ping, PartnerClose } })).ReadEnvelope()).Code());
        Assert.Equal(0, (await (await admin.PutJson($"/api/v1/integration/app/{appId}/scopes",
            new { bindings = new[] { new { scopeKey = "partner", allValues = false, values = new[] { "P9" } } } })).ReadEnvelope()).Code());
        var grants = await (await admin.GetAsync($"/api/v1/integration/app/{appId}/grants")).ReadEnvelope();
        Assert.Equal(2, grants.GetProperty("data").GetProperty("permissions").GetArrayLength());
        var bound = await (await admin.GetAsync($"/api/v1/integration/app/{appId}/scopes")).ReadEnvelope();
        Assert.Equal("合作方", bound.GetProperty("data")[0].GetProperty("scopeName").GetString());

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/integration/catalog/endpoints"));
        await AuthProbe.AssertForbidden(await limited.PutJson($"/api/v1/integration/app/{appId}/grants", new { permissions = Array.Empty<string>() }));

        using var scope = f.Services.CreateScope();
        var titles = await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<SysOpLog>().Select(l => l.Title).ToListAsync();
        Assert.Contains("修改接入应用授权", titles);
        Assert.Contains("修改接入应用数据范围", titles);

        // 后台 OpenAPI 文档(前端契约源)不含开放端点
        var adminDoc = await (await f.CreateClient().GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/integration/app/page", adminDoc);
        Assert.DoesNotContain("/api/open/", adminDoc);
    }

    [Fact]
    public async Task Authorization_changes_on_one_host_apply_to_another_host_on_next_request()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-itg-authz-{Guid.NewGuid():N}.db");
        try
        {
            using var hostA = new IntegrationAppFactory { DbPath = dbPath, WorkerId = 21, DeleteDbOnDispose = false };
            _ = hostA.CreateClient();
            using var hostB = new IntegrationAppFactory { DbPath = dbPath, ResetDatabase = false, WorkerId = 22, DeleteDbOnDispose = false };
            var (appId, key) = await NewAppAsync(hostA.Services, "multi", [Ping], []);
            var onB = hostB.OpenClient(key);

            Assert.Equal(0, (await onB.Send(HttpMethod.Get, "/api/open/v1/ping")).Body.Code());   // B 缓存了 v 版授权
            using (var scope = hostA.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IOpenAppAuthorizationService>()
                    .SetGrantsAsync(appId, new OpenAppGrantInput { Permissions = [] });
            Assert.Equal(IntegrationErrorCode.PermissionDenied, (await onB.Send(HttpMethod.Get, "/api/open/v1/ping")).Body.Code());

            using (var scope = hostA.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IIntegrationAppService>().SetEnabledAsync(appId, false);
            Assert.Equal(401, (await onB.Send(HttpMethod.Get, "/api/open/v1/ping")).Status);
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
            AdminAppFactory.TryDeleteWorkerIdLockDir(dbPath);
        }
    }

    [Fact]
    public void Startup_rules_reject_undeclared_misrouted_user_guarded_and_entity_returning_open_actions()
    {
        static bool Known(string? key) => key is "org" or "none" or "partner";

        var ok = Action(nameof(RuleProbe.Good), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org"));
        Assert.Empty(OpenApiConventionRules.Validate([ok], Known));

        var cases = new (ControllerActionDescriptor Action, string Expect)[]
        {
            (Action(nameof(RuleProbe.Good), "api/open/v1/probe", "GET", new OpenApiAttribute()), "OpenApiDataScope"),
            (Action(nameof(RuleProbe.Good), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("warehouse")), "warehouse"),
            (Action(nameof(RuleProbe.Good), "api/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "api/open/v{n}/"),
            (Action(nameof(RuleProbe.Good), "api/open/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "api/open/v{n}/"),
            (Action(nameof(RuleProbe.Good), "api/open/v1/probe", null, new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "HTTP 方法"),
            (Action(nameof(RuleProbe.Good), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org"), new RolePermissionAttribute()), "RolePermission"),
            (Action(nameof(RuleProbe.Good), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org"), new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute()), "AllowAnonymous"),
            (Action(nameof(RuleProbe.Good), "api/open/v1/probe", "GET"), "未标注 [OpenApi]"),
            (Action(nameof(RuleProbe.ReturnsEntity), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "SysUser"),
            (Action(nameof(RuleProbe.ReturnsDtoWithEntity), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "SysOrg"),
            (Action(nameof(RuleProbe.ReturnsDeepEntity), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "SysUser"),
            (Action(nameof(RuleProbe.ReturnsObject), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "object"),
            (Action(nameof(RuleProbe.ReturnsObjectDictionary), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "object"),
            (Action(nameof(RuleProbe.ReturnsUntypedEnumerable), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "IEnumerable"),
            (Action(nameof(RuleProbe.ReturnsActionResult), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "IActionResult"),
            (Action(nameof(RuleProbe.ReturnsBareDto), "api/open/v1/probe", "GET", new OpenApiAttribute(), new OpenApiDataScopeAttribute("org")), "Result<T>"),
        };
        Assert.Contains("object", OpenApiConventionRules.CheckOutputType(typeof(Result<object>))!);
        foreach (var (action, expect) in cases)
        {
            var errors = OpenApiConventionRules.Validate([action], Known);
            Assert.True(errors.Any(e => e.Contains(expect, StringComparison.Ordinal)),
                $"{action.MethodInfo.Name} 期望包含「{expect}」,实际:{string.Join(" | ", errors)}");
        }

        // 类级范围被方法级覆盖(元数据按类→方法顺序,取最后一个)
        var overridden = Action(nameof(RuleProbe.Good), "api/open/v1/probe", "GET",
            new OpenApiAttribute(), new OpenApiDataScopeAttribute("warehouse"), new OpenApiDataScopeAttribute("org"));
        Assert.Empty(OpenApiConventionRules.Validate([overridden], Known));
        Assert.Null(OpenApiConventionRules.CheckOutputType(typeof(Task)));
        Assert.Null(OpenApiConventionRules.CheckOutputType(typeof(Task<Result<PagedList<RuleProbe.SafeDto>>>)));
    }

    [Fact]
    public void Startup_rules_reject_user_endpoints_that_authenticate_with_the_open_app_scheme()
    {
        static bool Known(string? key) => key is "org" or "none" or "partner";

        // 非 [OpenApi] 的动作点名接入应用方案:接入凭据能进来,却没有应用授权与范围检查
        foreach (var metadata in new object[]
        {
            new AuthorizeAttribute { AuthenticationSchemes = OpenAppAuthenticationDefaults.Scheme },
            new AuthorizeAttribute { AuthenticationSchemes = "Bearer, " + OpenAppAuthenticationDefaults.Scheme },
            new AuthorizationPolicyBuilder(OpenAppAuthenticationDefaults.Scheme).RequireAuthenticatedUser().Build(),
        })
        {
            var errors = OpenApiConventionRules.Validate([Action(nameof(RuleProbe.Good), "api/v1/probe", "GET", metadata)], Known);
            Assert.Contains(errors, e => e.Contains(OpenAppAuthenticationDefaults.Scheme, StringComparison.Ordinal));
        }
        Assert.Empty(OpenApiConventionRules.Validate([Action(nameof(RuleProbe.Good), "api/v1/probe", "GET", new AuthorizeAttribute())], Known));
        Assert.Empty(OpenApiConventionRules.Validate(
            [Action(nameof(RuleProbe.Good), "api/v1/probe", "GET", new AuthorizeAttribute { AuthenticationSchemes = "Bearer" })], Known));

        var openAppPolicy = new AuthorizationPolicyBuilder(OpenAppAuthenticationDefaults.Scheme).RequireAuthenticatedUser().Build();
        var named = OpenApiConventionRules.Validate(
            [Action(nameof(RuleProbe.Good), "api/v1/probe", "GET", new AuthorizeAttribute { Policy = "partner" })],
            Known,
            name => name == "partner" ? openAppPolicy : null);
        Assert.Contains(named, e => e.Contains(OpenAppAuthenticationDefaults.Scheme, StringComparison.Ordinal));

        var viaDefault = OpenApiConventionRules.Validate(
            [Action(nameof(RuleProbe.Good), "api/v1/probe", "GET", new AuthorizeAttribute())],
            Known, defaultPolicy: openAppPolicy);
        Assert.Contains(viaDefault, e => e.Contains(OpenAppAuthenticationDefaults.Scheme, StringComparison.Ordinal));

        var viaFallback = OpenApiConventionRules.Validate(
            [Action(nameof(RuleProbe.Good), "api/v1/probe", "GET")],
            Known, fallbackPolicy: openAppPolicy);
        Assert.Contains(viaFallback, e => e.Contains(OpenAppAuthenticationDefaults.Scheme, StringComparison.Ordinal));
        Assert.Empty(OpenApiConventionRules.Validate(
            [Action(nameof(RuleProbe.Good), "api/v1/probe", "GET", new AllowAnonymousAttribute())],
            Known, fallbackPolicy: openAppPolicy));
    }

    [Fact]
    public async Task String_scope_values_are_bound_as_parameters_so_non_ascii_codes_match()
    {
        using var f = new IntegrationAppFactory();
        using (var scope = f.Services.CreateScope())
        {
            // 值走参数而不是内联字面量:SQL Server 上无 N 前缀的字面量会把非拉丁字符变成 ?,在拉丁排序规则下再也匹配不上
            var sql = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>()
                .Queryable<integrationhost::TenonAdmin.IntegrationTestHost.DemoTicket>()
                .WhereInScope(new OpenAppDataScope("partner", false, ["合作方甲", "P1"]), t => t.PartnerCode)
                .ToSql();
            Assert.DoesNotContain("合作方甲", sql.Key);
            Assert.Contains(sql.Value, p => Equals(p.Value, "合作方甲"));
        }

        var own = await SeedTicketAsync(f.Services, "甲的工单", "合作方甲", orgId: null);
        await SeedTicketAsync(f.Services, "乙的工单", "合作方乙", orgId: null);
        var (_, key) = await NewAppAsync(f.Services, "partner-cn", [PartnerList], [PartnerScope("合作方甲")]);
        var page = (await f.OpenClient(key).Send(HttpMethod.Get, "/api/open/v1/partner-tickets")).Body.GetProperty("data");
        Assert.Equal([own], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToArray());
    }

    private static ControllerActionDescriptor Action(string method, string template, string? verb, params object[] metadata)
    {
        var info = typeof(RuleProbe).GetMethod(method)!;
        return new ControllerActionDescriptor
        {
            ControllerName = "RuleProbe",
            ActionName = method,
            ControllerTypeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(RuleProbe)),
            MethodInfo = info,
            AttributeRouteInfo = new AttributeRouteInfo { Template = template },
            ActionConstraints = verb is null ? [] : [new HttpMethodActionConstraint([verb])],
            EndpointMetadata = metadata.ToList(),
        };
    }

    private static string KeyIdOf(string apiKey) =>
        OpenAppApiKey.TryParse(apiKey, out var keyId, out _) ? keyId : throw new ArgumentException("bad key");

    public sealed class RuleProbe
    {
        public Task<Result<SafeDto>> Good() => throw new NotSupportedException();
        public Task<Result<SysUser>> ReturnsEntity() => throw new NotSupportedException();
        public Result<UnsafeDto> ReturnsDtoWithEntity() => throw new NotSupportedException();
        public Task<IActionResult> ReturnsActionResult() => throw new NotSupportedException();
        public Task<SafeDto> ReturnsBareDto() => throw new NotSupportedException();
        public Result<DeepDto1> ReturnsDeepEntity() => throw new NotSupportedException();
        public Result<object> ReturnsObject() => throw new NotSupportedException();
        public Result<Dictionary<string, object>> ReturnsObjectDictionary() => throw new NotSupportedException();
        public Result<System.Collections.IEnumerable> ReturnsUntypedEnumerable() => throw new NotSupportedException();

        public sealed record SafeDto(long Id, string Name, DateTimeOffset At, IReadOnlyList<string> Tags);

        public sealed record UnsafeDto(long Id, SysOrg Org);

        public sealed record DeepDto1(DeepDto2 Value);
        public sealed record DeepDto2(DeepDto3 Value);
        public sealed record DeepDto3(DeepDto4 Value);
        public sealed record DeepDto4(DeepDto5 Value);
        public sealed record DeepDto5(SysUser Value);
    }
}

/// <summary>宿主级验证:开放端点漏声明数据范围时,宿主拒绝启动(启动校验真正挂在管线上,而不只是纯函数)。</summary>
public class IntegrationOpenApiStartupTests
{
    [Fact]
    public void Host_refuses_to_start_when_an_open_endpoint_is_misdeclared()
    {
        using var f = new IntegrationAppFactory
        {
            Overrides = s => s.AddControllers().AddApplicationPart(typeof(MisdeclaredOpenController).Assembly),
        };
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("MisdeclaredOpenController", ex.ToString());
        Assert.Contains("OpenApiDataScope", ex.ToString());
    }
}

/// <summary>故意漏声明 [OpenApiDataScope] 的开放控制器,只在上面的用例里作为应用部件挂入。</summary>
[ApiController]
[Route("api/open/v1/misdeclared")]
[OpenApi]
public class MisdeclaredOpenController : ControllerBase
{
    [HttpGet]
    public Result<string> Get() => Result<string>.Ok("x");
}
