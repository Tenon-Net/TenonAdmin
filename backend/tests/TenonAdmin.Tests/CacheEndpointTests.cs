using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 缓存管理 HTTP 契约。服务层清配置见 <see cref="CacheAdminTests"/>;
/// 这里锁四条路由的未登录、无权限、关闭模块,以及超管调用确实清掉哨兵并留操作日志。
/// </summary>
public class CacheEndpointTests
{
    private static async Task<HttpClient> SuperAdmin(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>只有 <c>GET:/api/v1/ping</c>(菜单 Id 2)的普通用户。</summary>
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

    [Fact]
    public async Task Anonymous_flush_is_401_and_40006()
    {
        using var f = new AdminAppFactory();
        var resp = await f.CreateClient().PostJson("/api/v1/sys/cache/flush-auth", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(40006, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task User_without_cache_permission_is_403_on_every_flush()
    {
        using var f = new AdminAppFactory();
        var c = await PingOnly(f);
        foreach (var path in new[]
        {
            "/api/v1/sys/cache/flush-auth",
            "/api/v1/sys/cache/flush-dict",
            "/api/v1/sys/cache/flush-config",
            "/api/v1/sys/cache/rebuild-portal",
        })
        {
            var resp = await c.PostJson(path, new { });
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Equal(41001, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
        }
    }

    [Fact]
    public async Task Disabled_module_hides_flush_routes()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Cache"] };
        var admin = await SuperAdmin(f);
        var resp = await admin.PostJson("/api/v1/sys/cache/flush-config", new { });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Super_admin_flush_clears_sentinels_and_writes_operation_log()
    {
        using var f = new AdminAppFactory();
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var cache = sp.GetRequiredService<ICacheProvider>();
            var configKey = (await sp.GetRequiredService<IRepository<SysConfig>>().AsQueryable()
                .Select(c => c.ConfigKey).ToListAsync()).First();
            var userId = (await sp.GetRequiredService<IRepository<SysUser>>().AsQueryable()
                .Select(u => u.Id).ToListAsync()).First();
            var dictCode = (await sp.GetRequiredService<IRepository<SysDictType>>().AsQueryable()
                .Select(t => t.Code).ToListAsync()).First();
            await cache.SetAsync(CacheKeys.Config(configKey), "sentinel");
            await cache.SetAsync(CacheKeys.UserPermissions(userId), "sentinel");
            await cache.SetAsync(CacheKeys.DictItems(dictCode), "sentinel");
        }

        var admin = await SuperAdmin(f);

        var config = await (await admin.PostJson("/api/v1/sys/cache/flush-config", new { })).ReadEnvelope();
        Assert.Equal(0, config.GetProperty("code").GetInt32());
        Assert.True(config.GetProperty("data").GetInt32() > 0);

        var auth = await (await admin.PostJson("/api/v1/sys/cache/flush-auth", new { })).ReadEnvelope();
        Assert.Equal(0, auth.GetProperty("code").GetInt32());
        Assert.True(auth.GetProperty("data").GetInt32() > 0);

        var dict = await (await admin.PostJson("/api/v1/sys/cache/flush-dict", new { })).ReadEnvelope();
        Assert.Equal(0, dict.GetProperty("code").GetInt32());
        Assert.True(dict.GetProperty("data").GetInt32() > 0);

        var portal = await (await admin.PostJson("/api/v1/sys/cache/rebuild-portal", new { })).ReadEnvelope();
        Assert.Equal(0, portal.GetProperty("code").GetInt32());
        var generation = portal.GetProperty("data").GetInt64();
        Assert.True(generation > 0);
        var again = await (await admin.PostJson("/api/v1/sys/cache/rebuild-portal", new { })).ReadEnvelope();
        Assert.Equal(generation + 1, again.GetProperty("data").GetInt64());

        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var cache = sp.GetRequiredService<ICacheProvider>();
            var configKey = (await sp.GetRequiredService<IRepository<SysConfig>>().AsQueryable()
                .Select(c => c.ConfigKey).ToListAsync()).First();
            var userId = (await sp.GetRequiredService<IRepository<SysUser>>().AsQueryable()
                .Select(u => u.Id).ToListAsync()).First();
            var dictCode = (await sp.GetRequiredService<IRepository<SysDictType>>().AsQueryable()
                .Select(t => t.Code).ToListAsync()).First();
            Assert.Null(await cache.GetAsync<string>(CacheKeys.Config(configKey)));
            Assert.Null(await cache.GetAsync<string>(CacheKeys.UserPermissions(userId)));
            Assert.Null(await cache.GetAsync<string>(CacheKeys.DictItems(dictCode)));
        }

        var logs = (await (await admin.GetAsync("/api/v1/sys/log/op/page?Current=1&Size=100")).ReadEnvelope())
            .GetProperty("data").GetProperty("items").EnumerateArray();
        Assert.Contains(logs, l => l.GetProperty("path").GetString() == "/api/v1/sys/cache/flush-config"
                                    && l.GetProperty("title").GetString() == "清缓存-配置");
    }
}
