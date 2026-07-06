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
    }

    /// <inheritdoc />
    public virtual async Task SetUserRolesAsync(long userId, IReadOnlyCollection<long> roleIds)
    {
        AdminException.ThrowIf(!await users.AnyAsync(u => u.Id == userId), ErrorCode.UserNotFound);

        var links = roleIds.Distinct().Select(rid => new SysUserRole { UserId = userId, RoleId = rid }).ToList();
        await ReplaceAsync(
            deleteExisting: () => userRoles.Db.Deleteable<SysUserRole>().Where(x => x.UserId == userId).ExecuteCommandAsync(),
            insertNew: links);

        await InvalidatePermissionsAsync([userId]);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<long>> GetRoleMenuIdsAsync(long roleId) =>
        await roleMenus.AsQueryable().Where(x => x.RoleId == roleId).Select(x => x.MenuId).ToListAsync();

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<long>> GetUserRoleIdsAsync(long userId) =>
        await userRoles.AsQueryable().Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync();

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
}
