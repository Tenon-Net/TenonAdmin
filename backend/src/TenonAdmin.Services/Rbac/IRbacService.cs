using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 角色-菜单授权服务(设计 §6,T1 纵切核心)——维护"用户↔角色""角色↔菜单"两组关联 + 角色数据范围,
/// 并在变更后<b>精确失效受影响用户的权限/数据范围缓存</b>,保证授权改动即时生效。
/// <para>类 public、方法 virtual:接外部权限中心或加自定义规则时继承覆写(设计 §5.3)。</para>
/// </summary>
public interface IRbacService
{
    /// <summary>设置某角色授予的菜单(全量替换)。之后失效所有挂该角色用户的权限缓存。</summary>
    Task SetRoleMenusAsync(long roleId, IReadOnlyCollection<long> menuIds);

    /// <summary>设置某用户拥有的角色(全量替换)。之后失效该用户的权限缓存。</summary>
    Task SetUserRolesAsync(long userId, IReadOnlyCollection<long> roleIds);

    /// <summary>取某角色当前授予的菜单 Id 集合</summary>
    Task<IReadOnlyCollection<long>> GetRoleMenuIdsAsync(long roleId);

    /// <summary>取某用户当前拥有的角色 Id 集合</summary>
    Task<IReadOnlyCollection<long>> GetUserRoleIdsAsync(long userId);

    /// <summary>设置某角色的数据范围(每角色一条,upsert)。自定义机构仅 <see cref="DataScopeType.Custom"/> 时有意义。
    /// 之后失效所有挂该角色用户的数据范围缓存。</summary>
    Task SetRoleDataScopeAsync(long roleId, DataScopeType scopeType, IReadOnlyCollection<long>? customOrgIds = null);

    /// <summary>取某角色的数据范围配置(未配置返回 null)</summary>
    Task<SysRoleDataScope?> GetRoleDataScopeAsync(long roleId);
}
