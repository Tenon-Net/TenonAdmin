namespace TenonAdmin.Services;

/// <summary>
/// 角色-菜单授权服务(设计 §6,T1 纵切核心)——维护"用户↔角色""角色↔菜单"两组关联,
/// 并在变更后<b>精确失效受影响用户的权限缓存</b>,保证授权改动即时生效。
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
}
