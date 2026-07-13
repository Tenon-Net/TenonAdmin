using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// "授权变更即时生效"三处缓存/会话失效遗漏的回归(审查报告 🟠)。设计承诺变更即精确失效,
/// 而非只靠 TTL 兜底;本组分别锁:停用用户吊销会话、改菜单失效权限缓存、动机构树失效数据范围缓存。
/// </summary>
public class CacheInvalidationTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>在宿主内造一个挂了指定权限码菜单的角色 + 普通用户,返回其登录账号/密码。</summary>
    private static async Task<(string account, string password)> SeedUser(AdminAppFactory f, long menuId)
    {
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var role = new SysRole { Name = "缓存失效测试角色", Code = "cache-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
        await sp.GetRequiredService<IRepository<SysRole>>().InsertAsync(role);
        await sp.GetRequiredService<IRbacService>().SetRoleMenusAsync(role.Id, [menuId]);

        var account = "cache-" + Guid.CreateVersion7().ToString("N")[..8];
        const string password = "Cache@123456";
        await sp.GetRequiredService<IUserService>().AddAsync(
            new AddUserInput { Account = account, Password = password, Name = "缓存用户", Enabled = true, RoleIds = [role.Id] });
        return (account, password);
    }

    [Fact]
    public async Task Disabling_user_revokes_active_sessions_immediately()
    {
        // 遗漏一:停用用户须即时下线其在线会话——原访问令牌下次请求即 401(此前要等令牌自然过期)。
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: 2);   // 授 GET:/api/v1/ping

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/ping")).StatusCode);   // 在线可用

        // 管理员停用该用户
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var uid = (await sp.GetRequiredService<IRepository<SysUser>>().GetFirstAsync(u => u.Account == account))!.Id;
            await sp.GetRequiredService<IUserService>().SetEnabledAsync(uid, false);
        }

        // 原令牌再访问受保护接口 → 401 + 40006(会话被吊销,即时生效)
        var after = await c.GetAsync("/api/v1/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(40006, (await after.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Editing_menu_invalidates_granted_users_permission_cache()
    {
        // 遗漏二:改菜单(停用/改权限码)须即时失效被授该菜单用户的权限缓存,不等 TTL。
        using var f = new AdminAppFactory();
        var (account, password) = await SeedUser(f, menuId: 2);   // 授 GET:/api/v1/ping

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/ping")).StatusCode);   // 首次请求预热 perm 缓存

        // 管理员经菜单服务停用菜单 2(经 MenuService.UpdateAsync → 扇出失效受影响用户 perm)
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var menu = (await sp.GetRequiredService<IRepository<SysMenu>>().GetByIdAsync(2))!;
            await sp.GetRequiredService<IMenuService>().UpdateAsync(2, new MenuInput
            {
                ParentId = menu.ParentId, Type = menu.Type, Title = menu.Title, Permission = menu.Permission,
                Sort = menu.Sort, Enabled = false, ModuleId = menu.ModuleId,
                Path = menu.Path, Component = menu.Component, Icon = menu.Icon, Visible = menu.Visible,
            });
        }

        // perm 缓存已失效并按新授权(菜单停用→不再授出该码)重算 → 同一会话原令牌现 403 + 41001
        var after = await c.GetAsync("/api/v1/ping");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        Assert.Equal(41001, (await after.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Editing_org_tree_invalidates_data_scope_cache()
    {
        // 遗漏三:机构树结构变更须失效数据范围缓存(受影响集难精确圈定 → 失效全体)。
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // 造普通用户并预热其数据范围缓存 scope:{uid}
        var uid = (await sp.GetRequiredService<IUserService>().AddAsync(
            new AddUserInput { Account = "scope-" + Guid.CreateVersion7().ToString("N")[..8], Password = "Scope@123456", Name = "范围用户", Enabled = true })).Id;

        var cache = sp.GetRequiredService<ICacheProvider>();   // Singleton:跨 scope 同一实例
        var key = CacheKeys.UserDataScope(uid);
        await sp.GetRequiredService<IDataScopeProvider>().ResolveAsync(uid);
        Assert.NotNull(await cache.GetAsync<DataScopeResult>(key));   // 已缓存

        // 机构树变更(新增顶级机构)→ 全体 scope 失效(含该用户)
        await sp.GetRequiredService<IOrgService>().AddAsync(
            new OrgInput { ParentId = 0, Name = "新机构", Code = "org-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true });

        Assert.Null(await cache.GetAsync<DataScopeResult>(key));      // 已失效,下次请求按新树重算
    }
}
