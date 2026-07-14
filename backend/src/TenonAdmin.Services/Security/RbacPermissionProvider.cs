using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IPermissionProvider"/> 的 RBAC 实现(设计 §5.5/§6)——取代占位的空实现。
/// <para>热路径优先走缓存:命中直接返回;未命中才聚合查库(用户→角色→菜单权限码并集),
/// 结果回填缓存。授权变更由 <see cref="RbacService"/> 精确失效对应用户的缓存键,故变更即时生效。</para>
/// <para>已缓存的<b>空集合</b>与"未缓存"可区分(见 <see cref="ICacheProvider.GetAsync{T}"/>),
/// 无权限用户也只查一次库,不会每请求穿透。</para>
/// </summary>
public class RbacPermissionProvider(
    IRepository<SysUserRole> userRoles,
    IRepository<SysRoleMenu> roleMenus,
    IRepository<SysMenu> menus,
    ICacheProvider cache,
    AdminCacheOptions cacheOptions) : IPermissionProvider
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(long userId, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.UserPermissions(userId);
        var cached = await cache.GetAsync<string[]>(key, cancellationToken);
        if (cached is not null) return cached;

        var codes = await LoadFromDatabaseAsync(userId);
        var ttl = cacheOptions.PermissionMinutes > 0 ? TimeSpan.FromMinutes(cacheOptions.PermissionMinutes) : (TimeSpan?)null;
        await cache.SetAsync(key, codes, ttl, cancellationToken);
        return codes;
    }

    /// <summary>
    /// 聚合查库:用户的角色 → 角色的菜单 → 菜单里带路由码且启用的节点的 Permission 去重。
    /// 三步分别短路(无角色/无菜单直接返回空),避免空 IN 查询;仅在缓存未命中时执行。
    /// </summary>
    protected virtual async Task<string[]> LoadFromDatabaseAsync(long userId)
    {
        var roleIds = await userRoles.AsQueryable()
            .InnerJoin<SysRole>((ur, r) => ur.RoleId == r.Id && r.Enabled)
            .Where((ur, r) => ur.UserId == userId)
            .Select((ur, r) => ur.RoleId).ToListAsync();
        if (roleIds.Count == 0) return [];

        var menuIds = await roleMenus.AsQueryable().Where(x => roleIds.Contains(x.RoleId)).Select(x => x.MenuId).ToListAsync();
        if (menuIds.Count == 0) return [];

        var codes = await menus.AsQueryable()
            .Where(m => menuIds.Contains(m.Id) && m.Enabled && m.Permission != "")
            .Select(m => m.Permission)
            .ToListAsync();
        return codes.Distinct().ToArray();
    }
}
