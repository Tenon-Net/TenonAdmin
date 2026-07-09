using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IRbacService"/> 默认实现。关联维护用"整删再插"全量替换语义:
/// 关联行是纯连接数据、无保留价值,故物理删除(走 <c>Db.Deleteable</c> 逃生舱口)而非软删——
/// 免得软删残行既撑大表又撞唯一索引。删+插包在事务里,失效缓存放在事务提交之后。
/// </summary>
public class RbacService(
    IRepository<SysRole> roles,
    IRepository<SysUser> users,
    IRepository<SysUserRole> userRoles,
    IRepository<SysRoleMenu> roleMenus,
    IRepository<SysRoleDataScope> roleScopes,
    ICacheProvider cache) : IRbacService
{
    /// <inheritdoc />
    public virtual async Task SetRoleMenusAsync(long roleId, IReadOnlyCollection<long> menuIds)
    {
        AdminException.ThrowIf(!await roles.AnyAsync(r => r.Id == roleId), ErrorCode.RoleNotFound);

        var links = menuIds.Distinct().Select(mid => new SysRoleMenu { RoleId = roleId, MenuId = mid }).ToList();
        await ReplaceAsync(
            deleteExisting: () => roleMenus.Db.Deleteable<SysRoleMenu>().Where(x => x.RoleId == roleId).ExecuteCommandAsync(),
            insertNew: links);

        // 授权变了 → 失效所有挂该角色用户的权限缓存(下次请求即按新授权重算)
        var affectedUsers = await userRoles.AsQueryable().Where(x => x.RoleId == roleId).Select(x => x.UserId).ToListAsync();
        await InvalidatePermissionsAsync(affectedUsers);
        await cache.IncrementAsync(CacheKeys.PortalGeneration);   // 授权变动改门户模块/菜单树 → 门户缓存整体失效
    }

    /// <inheritdoc />
    public virtual async Task SetUserRolesAsync(long userId, IReadOnlyCollection<long> roleIds)
    {
        AdminException.ThrowIf(!await users.AnyAsync(u => u.Id == userId), ErrorCode.UserNotFound);

        var links = roleIds.Distinct().Select(rid => new SysUserRole { UserId = userId, RoleId = rid }).ToList();
        await ReplaceAsync(
            deleteExisting: () => userRoles.Db.Deleteable<SysUserRole>().Where(x => x.UserId == userId).ExecuteCommandAsync(),
            insertNew: links);

        // 角色变了 → 权限与数据范围都可能变,两者缓存都失效
        await InvalidatePermissionsAsync([userId]);
        await InvalidateScopesAsync([userId]);
        await cache.IncrementAsync(CacheKeys.PortalGeneration);   // 用户角色变动改其门户模块/菜单树 → 门户缓存整体失效
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<long>> GetRoleMenuIdsAsync(long roleId) =>
        await roleMenus.AsQueryable().Where(x => x.RoleId == roleId).Select(x => x.MenuId).ToListAsync();

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<long>> GetUserRoleIdsAsync(long userId) =>
        await userRoles.AsQueryable().Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync();

    /// <inheritdoc />
    public virtual async Task SetRoleDataScopeAsync(long roleId, DataScopeType scopeType, IReadOnlyCollection<long>? customOrgIds = null)
    {
        AdminException.ThrowIf(!await roles.AnyAsync(r => r.Id == roleId), ErrorCode.RoleNotFound);

        // 自定义机构仅 Custom 有意义;非 Custom 一律清空,避免残留误导
        var csv = scopeType == DataScopeType.Custom && customOrgIds is { Count: > 0 }
            ? string.Join(',', customOrgIds.Distinct())
            : "";

        var existing = await roleScopes.GetFirstAsync(x => x.RoleId == roleId);
        if (existing is null)
            await roleScopes.InsertAsync(new SysRoleDataScope { RoleId = roleId, ScopeType = scopeType, CustomOrgIds = csv });
        else
        {
            existing.ScopeType = scopeType;
            existing.CustomOrgIds = csv;
            await roleScopes.UpdateAsync(existing);
        }

        var affectedUsers = await userRoles.AsQueryable().Where(x => x.RoleId == roleId).Select(x => x.UserId).ToListAsync();
        await InvalidateScopesAsync(affectedUsers);
    }

    /// <inheritdoc />
    public virtual Task<SysRoleDataScope?> GetRoleDataScopeAsync(long roleId) =>
        roleScopes.GetFirstAsync(x => x.RoleId == roleId);

    /// <inheritdoc />
    public virtual async Task InvalidatePermissionsByMenuAsync(long menuId)
    {
        // 菜单 → 授它的角色(sys_role_menu) → 挂这些角色的用户(sys_user_role) → 失效其权限缓存。
        // 菜单 CRUD 低频,过量失效无害(下次请求按新授权重算);软删菜单不清 sys_role_menu,故删后此扇出仍能命中受影响用户。
        var roleIds = await roleMenus.AsQueryable().Where(x => x.MenuId == menuId).Select(x => x.RoleId).ToListAsync();
        if (roleIds.Count == 0) return;
        var affectedUsers = await userRoles.AsQueryable().Where(x => roleIds.Contains(x.RoleId)).Select(x => x.UserId).ToListAsync();
        await InvalidatePermissionsAsync(affectedUsers);
    }

    /// <inheritdoc />
    public virtual async Task InvalidateAllScopesAsync()
    {
        // 机构树结构变更影响"本机构及以下/自定义"范围的解析结果,受影响用户集难以精确圈定(跨角色、跨层级)。
        // ponytail: 机构增改删极低频,直接失效全体用户 scope 最简且正确;若用户量巨大 + Redis 使 N 次删成本显著,
        //           再收窄为"仅 OrgAndChildren/Custom 范围角色所属用户",或改 scope 代际(generation)键做 O(1) 失效。
        var allUserIds = await users.AsQueryable().Select(u => u.Id).ToListAsync();
        await InvalidateScopesAsync(allUserIds);
    }

    /// <summary>事务内"整删再插"。任一步失败整体回滚,关联不会处于半更新状态。</summary>
    private async Task ReplaceAsync<TLink>(Func<Task<int>> deleteExisting, List<TLink> insertNew) where TLink : BaseEntity, new()
    {
        var result = await roleMenus.Db.Ado.UseTranAsync(async () =>
        {
            await deleteExisting();
            if (insertNew.Count > 0) await roleMenus.Db.Insertable(insertNew).ExecuteCommandAsync();
        });
        if (!result.IsSuccess) throw result.ErrorException;
    }

    private async Task InvalidatePermissionsAsync(IEnumerable<long> userIds)
    {
        foreach (var uid in userIds)
            await cache.RemoveAsync(CacheKeys.UserPermissions(uid));
    }

    private async Task InvalidateScopesAsync(IEnumerable<long> userIds)
    {
        foreach (var uid in userIds)
            await cache.RemoveAsync(CacheKeys.UserDataScope(uid));
    }
}
