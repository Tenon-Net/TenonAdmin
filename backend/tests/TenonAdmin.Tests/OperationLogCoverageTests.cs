using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 操作日志覆盖面(A1 回归锁):过滤器是 opt-out 的——<b>写操作默认留痕</b>,不依赖有人记得挂 [OperationLog]。
/// <para>此前是 opt-in,导致角色授权/数据范围/系统配置/停用用户/强制下线这些最该审计的动作全部无痕,
/// 而分片上传每片都留痕。本测试锁死"改回 opt-in 即变红"。</para>
/// </summary>
public class OperationLogCoverageTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>取操作日志首页全部条目(倒序,足量一页)。</summary>
    private static async Task<List<JsonElement>> OpLogs(HttpClient admin)
    {
        var page = await (await admin.GetAsync("/api/v1/sys/log/op/page?Current=1&Size=100")).ReadEnvelope();
        return page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
    }

    private static bool HasEntry(List<JsonElement> logs, string method, string path) =>
        logs.Any(l => l.GetProperty("httpMethod").GetString() == method
                   && l.GetProperty("path").GetString() == path);

    [Fact]
    public async Task Write_endpoints_without_marker_are_logged_with_route_as_title()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 建个角色,再给它设数据范围 —— 两个端点都没挂 [OperationLog],此前完全无痕
        var add = await (await admin.PostJson("/api/v1/sys/role/add",
            new { name = "auditor", code = "auditor", sort = 0, enabled = true, remark = "" })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var roleId = add.GetProperty("data").GetInt64();

        Assert.Equal(0, (await (await admin.PutJson("/api/v1/sys/role/datascope",
            new { roleId, scopeType = 1, customOrgIds = Array.Empty<long>() })).ReadEnvelope()).GetProperty("code").GetInt32());

        var logs = await OpLogs(admin);
        Assert.True(HasEntry(logs, "POST", "/api/v1/sys/role/add"), "新增角色未留痕");
        Assert.True(HasEntry(logs, "PUT", "/api/v1/sys/role/datascope"), "设置数据范围未留痕(权限系统里最该审计的动作)");

        // 未标注时"操作名"回落为规范化路由串 = 权限码,与角色授权页勾的那一条对齐
        var scope = logs.First(l => l.GetProperty("path").GetString() == "/api/v1/sys/role/datascope");
        Assert.Equal("PUT:/api/v1/sys/role/datascope", scope.GetProperty("title").GetString());
        Assert.Equal(0, scope.GetProperty("resultCode").GetInt32());
        Assert.True(scope.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Marker_still_overrides_title()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        await admin.PostJson("/api/v1/sys/user",
            new { account = "erin", password = "InitPass123", name = "Erin", enabled = true, roleIds = Array.Empty<long>() });

        var logs = await OpLogs(admin);
        var entry = logs.First(l => l.GetProperty("path").GetString() == "/api/v1/sys/user");
        Assert.Equal("新增用户", entry.GetProperty("title").GetString());   // [OperationLog] 仍是标题覆写
        Assert.Contains("***", entry.GetProperty("paramJson").GetString());  // 口令仍被脱敏
    }

    [Fact]
    public async Task Reads_and_anonymous_endpoints_stay_out_of_the_log()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var anon = f.CreateClient();

        // 读操作:列表分页若入日志,日志表会被自己刷爆
        await admin.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");
        // 匿名端点:登录已有专门的登录日志,且入参含明文口令
        await anon.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "wrong-on-purpose" });

        var logs = await OpLogs(admin);
        Assert.False(HasEntry(logs, "GET", "/api/v1/sys/user/page"), "读操作不该进操作日志");
        Assert.False(HasEntry(logs, "POST", "/api/v1/auth/login"), "登录不该进操作日志(已有登录日志)");
    }

    [Fact]
    public async Task Endpoints_with_their_own_audit_stay_out_even_when_marked()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.AddControllers().ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new ProbeControllers())),
        };
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync("/api/v1/test/oplog-probe/audited", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync("/api/v1/test/oplog-probe/marked", null)).StatusCode);

        using var scope = f.Services.CreateScope();
        var paths = await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<SysOpLog>().Select(l => l.Path).ToListAsync();
        Assert.Contains("/api/v1/test/oplog-probe/marked", paths);           // 对照:同样挂 [OperationLog] 的写操作照常留痕
        Assert.DoesNotContain("/api/v1/test/oplog-probe/audited", paths);    // 端点自带专用审计:连 [OperationLog] 标注也压过
    }

    /// <summary>只把探针控制器挂进宿主(不把整个测试程序集当应用部件)。</summary>
    private sealed class ProbeControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var probe = IntrospectionExtensions.GetTypeInfo(typeof(OperationLogProbeController));
            if (!feature.Controllers.Contains(probe)) feature.Controllers.Add(probe);
        }
    }
}

/// <summary>操作日志豁免(<see cref="ISkipOperationLogMetadata"/>)的探针端点,只在上面的用例里经特性提供者挂入。</summary>
[ApiController]
[Route("api/v1/test/oplog-probe")]
[AllowAnonymous]
public class OperationLogProbeController : ControllerBase
{
    [HttpPost("audited")]
    [OperationLog("自带审计的写操作")]
    [OwnAudit]
    public Result<bool> Audited() => Result<bool>.Ok(true);

    [HttpPost("marked")]
    [OperationLog("普通标注的写操作")]
    public Result<bool> Marked() => Result<bool>.Ok(true);

    /// <summary>端点已有等价的专用审计。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OwnAuditAttribute : Attribute, ISkipOperationLogMetadata;
}
