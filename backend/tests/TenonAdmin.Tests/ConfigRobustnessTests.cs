using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 配置接口的鉴权与失败契约。成功路径、站点匿名白名单和种子删除保护见
/// <see cref="ConfigCenterTests"/> 与 <see cref="PasswordPolicyEndpointTests"/>。
/// </summary>
public class ConfigRobustnessTests
{
    [Fact]
    public async Task Anonymous_cannot_read_or_write_protected_config_routes()
    {
        using var f = new AdminAppFactory();
        var anon = f.CreateClient();

        foreach (var resp in new[]
        {
            await anon.GetAsync("/api/v1/sys/config/page?Current=1&Size=10"),
            await anon.GetAsync("/api/v1/sys/config/1"),
            await anon.GetAsync("/api/v1/sys/config/value/sys.site.title"),
            await anon.GetAsync("/api/v1/sys/config/password-policy"),
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.Equal(40006, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
        }

        foreach (var resp in new[]
        {
            await anon.PutJson("/api/v1/sys/config/batch", new[] { new { configKey = "sys.site.title", configValue = "x" } }),
            await anon.PostJson("/api/v1/sys/config", new { configKey = "anon.key", name = "x" }),
            await anon.PutJson("/api/v1/sys/config/1", new { configKey = "sys.site.title", name = "x" }),
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.Equal(40006, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
        }

        var del = await anon.DeleteAsync("/api/v1/sys/config/1");
        Assert.Equal(HttpStatusCode.Unauthorized, del.StatusCode);
        Assert.Equal(40006, (await del.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Limited_user_is_forbidden_on_admin_routes_but_can_read_password_policy()
    {
        using var f = new AdminAppFactory();
        var c = await PingOnly(f);

        var page = await c.GetAsync("/api/v1/sys/config/page?Current=1&Size=10");
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Equal(41001, (await page.ReadEnvelope()).GetProperty("code").GetInt32());

        var policy = await c.GetAsync("/api/v1/sys/config/password-policy");
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        var policyBody = await policy.ReadEnvelope();
        Assert.Equal(0, policyBody.GetProperty("code").GetInt32());
        Assert.True(policyBody.GetProperty("data").GetProperty("minLength").GetInt32() >= 1);
    }

    [Fact]
    public async Task Missing_id_duplicate_key_and_missing_value_use_config_error_codes()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdmin(f);

        var missing = await (await admin.GetAsync("/api/v1/sys/config/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ConfigNotFound, missing.GetProperty("code").GetInt32());

        var updateMissing = await (await admin.PutJson("/api/v1/sys/config/999999999",
            new { configKey = "ignored", name = "不存在" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ConfigNotFound, updateMissing.GetProperty("code").GetInt32());

        var deleteMissing = await (await admin.DeleteAsync("/api/v1/sys/config/999999999")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ConfigNotFound, deleteMissing.GetProperty("code").GetInt32());

        var absent = await (await admin.GetAsync("/api/v1/sys/config/value/does.not.exist")).ReadEnvelope();
        Assert.Equal(0, absent.GetProperty("code").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, absent.GetProperty("data").ValueKind);

        var known = await (await admin.GetAsync("/api/v1/sys/config/value/sys.site.title")).ReadEnvelope();
        Assert.Equal(0, known.GetProperty("code").GetInt32());
        Assert.Equal("TenonAdmin", known.GetProperty("data").GetString());

        var dupSeed = await (await admin.PostJson("/api/v1/sys/config",
            new { configKey = "sys.site.title", name = "重复站点标题" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ConfigKeyExists, dupSeed.GetProperty("code").GetInt32());

        var add = await (await admin.PostJson("/api/v1/sys/config",
            new { configKey = "custom.once", name = "一次", configValue = "1" })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var id = add.GetProperty("data").GetInt64();
        var dupCustom = await (await admin.PostJson("/api/v1/sys/config",
            new { configKey = "custom.once", name = "再次" })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ConfigKeyExists, dupCustom.GetProperty("code").GetInt32());

        var removed = await (await admin.DeleteAsync($"/api/v1/sys/config/{id}")).ReadEnvelope();
        Assert.Equal(0, removed.GetProperty("code").GetInt32());
        var after = await (await admin.GetAsync($"/api/v1/sys/config/{id}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ConfigNotFound, after.GetProperty("code").GetInt32());

        var emptyBatch = await (await admin.PutJson("/api/v1/sys/config/batch", Array.Empty<object>())).ReadEnvelope();
        Assert.Equal(0, emptyBatch.GetProperty("code").GetInt32());
        Assert.True(emptyBatch.GetProperty("data").GetBoolean());
    }

    [Fact]
    public async Task Disabled_module_hides_site_and_admin_routes()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Config"] };
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/v1/sys/config/site")).StatusCode);

        var admin = await SuperAdmin(f);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/sys/config/page")).StatusCode);
    }

    private static async Task<HttpClient> SuperAdmin(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<HttpClient> PingOnly(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var role = new SysRole
        {
            Name = "受限角色",
            Code = "limited-" + Guid.CreateVersion7().ToString("N")[..8],
            Enabled = true,
        };
        await roles.InsertAsync(role);
        await sp.GetRequiredService<IRbacService>().SetRoleMenusAsync(role.Id, [2]);
        var account = "limited-" + Guid.CreateVersion7().ToString("N")[..8];
        const string password = "Limited@123456";
        await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = account,
            Password = password,
            Name = "受限用户",
            Enabled = true,
            RoleIds = [role.Id],
        });
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));
        return c;
    }
}
