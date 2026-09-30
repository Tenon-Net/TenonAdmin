using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using static TenonAdmin.Tests.IntegrationTestSupport;

namespace TenonAdmin.Tests;

/// <summary>
/// G02:接入应用管理后台接口走既有用户权限体系(未登录 401、无权限 403、超管可用),
/// 写操作进入用户操作审计;模块未启用时不暴露端点、不建表。
/// </summary>
public class IntegrationAdminApiTests
{
    [Fact]
    public async Task Admin_routes_require_login_and_route_permission()
    {
        using var f = new IntegrationAppFactory();
        var anonymous = f.CreateClient();
        await AuthProbe.AssertUnauthorized(await anonymous.GetAsync("/api/v1/integration/app/page"));
        await AuthProbe.AssertUnauthorized(await anonymous.PostJson("/api/v1/integration/app", new { code = "x1", name = "x" }));

        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/integration/app/page"));
        await AuthProbe.AssertForbidden(await limited.PostJson("/api/v1/integration/app", new { code = "x1", name = "x" }));
        await AuthProbe.AssertForbidden(await limited.PostJson("/api/v1/integration/app/1/credentials", new { }));
        await AuthProbe.AssertForbidden(await limited.PostJson("/api/v1/integration/app/1/credentials/1/revoke", new { }));
        await AuthProbe.AssertForbidden(await limited.DeleteAsync("/api/v1/integration/app/1/credentials/1"));
    }

    [Fact]
    public async Task Full_lifecycle_over_http_is_audited_as_user_operations()
    {
        using var f = new IntegrationAppFactory();
        var admin = await AuthProbe.SuperAdmin(f);

        var add = await (await admin.PostJson("/api/v1/integration/app", new { code = "crm-sync", name = "CRM 同步", description = "夜间同步" })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var appId = add.GetProperty("data").GetInt64();

        var duplicate = await (await admin.PostJson("/api/v1/integration/app", new { code = "crm-sync", name = "重复" })).ReadEnvelope();
        Assert.Equal(IntegrationErrorCode.AppCodeExists, duplicate.GetProperty("code").GetInt32());
        Assert.Equal("error.code.49011", duplicate.GetProperty("msgKey").GetString());

        var issued = await (await admin.PostJson($"/api/v1/integration/app/{appId}/credentials", new { name = "生产" })).ReadEnvelope();
        var credentialId = issued.GetProperty("data").GetProperty("credential").GetProperty("id").GetInt64();
        var apiKey = issued.GetProperty("data").GetProperty("apiKey").GetString();
        Assert.True((await IntegrationTestSupport.ValidateAsync(f.Services, apiKey)).IsValid);

        var expiry = await (await admin.PutJson($"/api/v1/integration/app/{appId}/credentials/{credentialId}/expiry",
            new { expiresAt = DateTimeOffset.UtcNow.AddDays(2) })).ReadEnvelope();
        Assert.Equal(0, expiry.GetProperty("code").GetInt32());

        var disable = await (await admin.PostJson($"/api/v1/integration/app/{appId}/disable", new { })).ReadEnvelope();
        Assert.Equal(0, disable.GetProperty("code").GetInt32());
        Assert.Equal(OpenAppCredentialFailure.AppDisabled, (await IntegrationTestSupport.ValidateAsync(f.Services, apiKey)).Failure);

        var page = await (await admin.GetAsync("/api/v1/integration/app/page?enabled=false")).ReadEnvelope();
        Assert.Equal(1, page.GetProperty("data").GetProperty("total").GetInt32());
        var row = page.GetProperty("data").GetProperty("items")[0];
        Assert.False(row.GetProperty("enabled").GetBoolean());
        Assert.Equal(1, row.GetProperty("activeCredentialCount").GetInt32());

        await admin.PostJson($"/api/v1/integration/app/{appId}/enable", new { });
        await admin.PostJson($"/api/v1/integration/app/{appId}/credentials/{credentialId}/revoke", new { });
        var detail = await (await admin.GetAsync($"/api/v1/integration/app/{appId}")).ReadEnvelope();
        var credential = detail.GetProperty("data").GetProperty("credentials")[0];
        Assert.Equal((int)OpenAppCredentialStatus.Revoked, credential.GetProperty("status").GetInt32());
        Assert.True(credential.GetProperty("revokedBy").GetInt64() > 0);   // 撤销人 = 当前管理员

        var deletedCredential = await (await admin.DeleteAsync($"/api/v1/integration/app/{appId}/credentials/{credentialId}")).ReadEnvelope();
        Assert.Equal(0, deletedCredential.GetProperty("code").GetInt32());
        var credentials = await (await admin.GetAsync($"/api/v1/integration/app/{appId}/credentials")).ReadEnvelope();
        Assert.Empty(credentials.GetProperty("data").EnumerateArray());
        Assert.Equal(OpenAppCredentialFailure.NotFound, (await IntegrationTestSupport.ValidateAsync(f.Services, apiKey)).Failure);

        var deleted = await (await admin.DeleteAsync($"/api/v1/integration/app/{appId}")).ReadEnvelope();
        Assert.Equal(0, deleted.GetProperty("code").GetInt32());
        var gone = await (await admin.GetAsync($"/api/v1/integration/app/{appId}")).ReadEnvelope();
        Assert.Equal(IntegrationErrorCode.AppNotFound, gone.GetProperty("code").GetInt32());

        using var scope = f.Services.CreateScope();
        var titles = await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>()
            .Queryable<SysOpLog>().Select(l => l.Title).ToListAsync();
        foreach (var title in new[] { "新增接入应用", "发放接入凭据", "调整接入凭据到期", "停用接入应用", "启用接入应用", "撤销接入凭据", "删除接入凭据", "删除接入应用" })
            Assert.Contains(title, titles);
    }

    [Fact]
    public async Task Only_super_admins_can_expand_an_apps_reach()
    {
        using var f = new IntegrationAppFactory();
        var org = await IntegrationOpenApiTestKit.NewOrgAsync(f.Services, "归属机构");
        var op = await IntegrationOperatorAsync(f);

        // 元数据与止损只看路由权限:非超管可以新建停用应用、改名称/说明/限流、停用
        var created = (await op.Send(HttpMethod.Post, "/api/v1/integration/app", new { code = "op-app", name = "运维应用", enabled = false })).Body;
        Assert.Equal(0, created.Code());
        var appId = created.GetProperty("data").GetInt64();
        Assert.Equal(0, (await op.Send(HttpMethod.Put, $"/api/v1/integration/app/{appId}", new { name = "改名", description = "说明", rateLimitPerMinute = 10 })).Body.Code());
        Assert.Equal(0, (await op.Send(HttpMethod.Post, $"/api/v1/integration/app/{appId}/disable", new { })).Body.Code());
        // 系统上下文(无登录用户)视为可信
        var credential = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IOpenAppCredentialService>().CreateAsync(appId, new OpenAppCredentialCreateInput()));
        var credentialId = credential.Credential.Id;

        // 扩大或恢复应用访问能力的操作:路由权限齐全的非超管同样被拒(41003),状态不变
        var expanding = new (HttpMethod Method, string Url, object Body)[]
        {
            (HttpMethod.Put, $"/api/v1/integration/app/{appId}/grants", new { permissions = new[] { IntegrationOpenApiTestKit.Ping } }),
            (HttpMethod.Put, $"/api/v1/integration/app/{appId}/scopes", new { bindings = new[] { new { scopeKey = OpenApiDataScopes.Org, allValues = true } } }),
            (HttpMethod.Post, $"/api/v1/integration/app/{appId}/credentials", new { name = "越权发放" }),
            (HttpMethod.Post, $"/api/v1/integration/app/{appId}/credentials/{credentialId}/rotate", new { }),
            (HttpMethod.Put, $"/api/v1/integration/app/{appId}/credentials/{credentialId}/expiry", new { neverExpires = true }),
            (HttpMethod.Post, $"/api/v1/integration/app/{appId}/enable", new { }),
            (HttpMethod.Put, $"/api/v1/integration/app/{appId}", new { name = "改名", ownerOrgId = org }),
            (HttpMethod.Post, "/api/v1/integration/app", new { code = "op-owned", name = "带归属机构", ownerOrgId = org }),
            (HttpMethod.Post, "/api/v1/integration/app", new { code = "op-enabled", name = "显式启用", enabled = true }),
            (HttpMethod.Post, "/api/v1/integration/app", new { code = "op-default-enabled", name = "默认启用" }),
        };
        foreach (var (method, url, body) in expanding)
        {
            var code = (await op.Send(method, url, body)).Body.Code();
            Assert.True(code == (int)ErrorCode.SuperAdminRequired, $"{method} {url} → {code}");
        }
        var unchanged = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IIntegrationAppService>().GetAsync(appId));
        Assert.False(unchanged.Enabled);
        Assert.Null(unchanged.OwnerOrgId);
        Assert.Equal(credential.Credential.ExpiresAt, Assert.Single(unchanged.Credentials).ExpiresAt);
        Assert.Empty((await InScopeAsync(f.Services, sp => sp.GetRequiredService<IOpenAppAuthorizationService>().GetGrantsAsync(appId))).Permissions);
        Assert.Empty(await InScopeAsync(f.Services, sp => sp.GetRequiredService<IOpenAppAuthorizationService>().GetScopesAsync(appId)));

        // 超管照常
        var admin = await AuthProbe.SuperAdmin(f);
        foreach (var (method, url, body) in expanding)
        {
            var code = (await admin.Send(method, url, body)).Body.Code();
            Assert.True(code == 0, $"超管 {method} {url} → {code}");
        }

        // 止损同样只看路由权限:撤销、删除
        Assert.Equal(0, (await op.Send(HttpMethod.Post, $"/api/v1/integration/app/{appId}/credentials/{credentialId}/revoke", new { })).Body.Code());
        Assert.Equal(0, (await op.Send(HttpMethod.Delete, $"/api/v1/integration/app/{appId}")).Body.Code());
    }

    [Fact]
    public async Task Org_scope_options_follow_the_callers_data_scope()
    {
        using var f = new IntegrationAppFactory();
        var mine = await IntegrationOpenApiTestKit.NewOrgAsync(f.Services, "本部");
        var child = await IntegrationOpenApiTestKit.NewOrgAsync(f.Services, "本部下属", mine);
        var other = await IntegrationOpenApiTestKit.NewOrgAsync(f.Services, "外部");
        var op = await IntegrationOperatorAsync(f, orgId: mine, scope: DataScopeType.OrgAndChildren);

        static async Task<HashSet<string>> Values(HttpClient client) =>
            (await client.Send(HttpMethod.Get, "/api/v1/integration/catalog/scopes/org/options")).Body
                .GetProperty("data").EnumerateArray().Select(o => o.GetProperty("value").GetString()!).ToHashSet();

        // 与内核机构树同一可见性:非超管只看到数据范围内的机构(及拼树用的祖先)
        var visible = await Values(op);
        Assert.Contains(mine.ToString(), visible);
        Assert.Contains(child.ToString(), visible);
        Assert.DoesNotContain(other.ToString(), visible);
        Assert.Contains(other.ToString(), await Values(await AuthProbe.SuperAdmin(f)));
    }

    [Fact]
    public void Deep_link_filters_are_backed_by_indexes()
    {
        static bool Indexed(Type entity, string column) =>
            entity.GetCustomAttributes<SugarIndexAttribute>().Any(i => i.IndexFields.Keys.First() == column);

        Assert.True(Indexed(typeof(IntegrationDelivery), nameof(IntegrationDelivery.BusinessKey)));        // 业务页 → 可靠投递 ?businessKey=
        Assert.True(Indexed(typeof(IntegrationDelivery), nameof(IntegrationDelivery.DeliveryKey)));        // ?deliveryKey=
        Assert.True(Indexed(typeof(IntegrationDelivery), nameof(IntegrationDelivery.Status)));             // ?status=
        Assert.True(Indexed(typeof(IntegrationOutboundLog), nameof(IntegrationOutboundLog.CallId)));       // 尝试记录 → 出站记录 ?callId=
        Assert.True(Indexed(typeof(IntegrationOutboundLog), nameof(IntegrationOutboundLog.DeliveryId)));   // ?deliveryId=
        Assert.True(Indexed(typeof(IntegrationInboundLog), nameof(IntegrationInboundLog.AppId)));          // 接入应用 → 开放调用记录 ?appId=
    }

    [Fact]
    public async Task Seeded_menu_buttons_match_every_admin_route_exactly()
    {
        using var f = new IntegrationAppFactory();
        _ = f.CreateClient();
        var actions = f.Services.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()
            .Where(a => a.ControllerTypeInfo.Assembly == typeof(IntegrationSetup).Assembly
                        && a.EndpointMetadata.OfType<TenonAdmin.AspNetCore.RolePermissionAttribute>().Any());
        var routeCodes = actions
            .SelectMany(a => TenonAdmin.Integration.OpenApiCatalog.MethodsOf(a)
                .Select(m => TenonAdmin.AspNetCore.PermissionCode.Build(m, a.AttributeRouteInfo!.Template)))
            .ToHashSet();

        using var seedScope = f.Services.CreateScope();
        var seed = seedScope.ServiceProvider.GetServices<TenonAdmin.SqlSugar.ISeedData>().OfType<TenonAdmin.SqlSugar.ISeedData<SysMenu>>()
            .Single(s => s.GetType().Assembly == typeof(IntegrationSetup).Assembly);
        var menus = seed.HasData().ToList();
        var seededCodes = menus.Where(m => m.Type == MenuType.Button).Select(m => m.Permission).ToHashSet();

        Assert.NotEmpty(routeCodes);
        Assert.Equal(routeCodes.OrderBy(c => c), seededCodes.OrderBy(c => c));          // 双向一致:无漏种、无错码
        Assert.Equal(menus.Count, menus.Select(m => m.Id).Distinct().Count());
        Assert.All(menus, m => Assert.True(m.Id >= TenonAdmin.Core.TenonSeedIds.ConsumerMin));

        // 菜单已落库,且挂在内置「系统」应用
        using var scope = f.Services.CreateScope();
        var root = await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<SysMenu>()
            .Where(m => m.Title == "第三方接入" && m.ParentId == 0).FirstAsync();
        Assert.Equal(1, root.ModuleId);
    }

    [Fact]
    public async Task Module_not_enabled_exposes_no_routes_and_creates_no_tables()
    {
        using var f = new AdminAppFactory();
        var admin = await AuthProbe.SuperAdmin(f);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/integration/app/page")).StatusCode);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.False(db.DbMaintenance.IsAnyTable("itg_app", false));
        Assert.False(db.DbMaintenance.IsAnyTable("itg_app_credential", false));
        Assert.Null(scope.ServiceProvider.GetService<IOpenAppCredentialValidator>());
        Assert.False(await db.Queryable<SysMenu>().AnyAsync(m => m.Title == "第三方接入"));        // 无不可用菜单
        Assert.False(await db.Queryable<SysJob>().AnyAsync(j => j.Code.StartsWith("itg-")));     // 无后台任务
    }

    [Fact]
    public async Task A_host_that_only_references_the_package_exposes_no_module_routes()
    {
        // Web SDK 宿主引用本包即自动把它加成应用部件;这里照样挂入,但既不调 AddTenonAdminIntegration 也不调 UseIntegration
        var module = typeof(IntegrationSetup).Assembly;
        using var f = new AdminAppFactory { Overrides = s => s.AddControllers().AddApplicationPart(module) };
        var admin = await AuthProbe.SuperAdmin(f);

        var exposed = f.Services.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()
            .Where(a => a.ControllerTypeInfo.Assembly == module)
            .Select(a => a.AttributeRouteInfo?.Template)
            .ToList();
        Assert.Empty(exposed);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/integration/app/page")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClient().GetAsync("/api/open/v1/whoami")).StatusCode);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.False(db.DbMaintenance.IsAnyTable("itg_app", false));
        Assert.False(db.DbMaintenance.IsAnyTable("itg_delivery", false));
    }

    /// <summary>非超管操作员:角色持有「第三方接入」下全部按钮(路由权限齐全);可选归属机构与角色数据范围。</summary>
    private static async Task<HttpClient> IntegrationOperatorAsync(IntegrationAppFactory f, long? orgId = null, DataScopeType? scope = null)
    {
        var account = "itg-op-" + Guid.CreateVersion7().ToString("N")[..8];
        const string password = "Operator@123456";
        using (var serviceScope = f.Services.CreateScope())
        {
            var sp = serviceScope.ServiceProvider;
            var menuIds = await sp.GetRequiredService<ISqlSugarClient>().Queryable<SysMenu>()
                .Where(m => m.Permission.Contains("/api/v1/integration/")).Select(m => m.Id).ToListAsync();
            var role = new SysRole { Name = "集成管理员", Code = account, Enabled = true };
            await sp.GetRequiredService<IRepository<SysRole>>().InsertAsync(role);
            var rbac = sp.GetRequiredService<IRbacService>();
            await rbac.SetRoleMenusAsync(role.Id, menuIds);
            if (scope is { } scopeType) await rbac.SetRoleDataScopeAsync(role.Id, scopeType);
            await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = account, Password = password, Name = "集成管理员", Enabled = true, OrgId = orgId, RoleIds = [role.Id],
            });
        }
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, password));
        return client;
    }
}
