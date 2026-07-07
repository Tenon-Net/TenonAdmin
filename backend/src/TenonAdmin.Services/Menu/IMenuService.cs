namespace TenonAdmin.Services;

/// <summary>
/// 菜单/门户服务(多应用门户)——面向<b>当前登录用户</b>的模块列表与按模块的侧边栏菜单树。
/// <para>模块访问权由菜单授权<b>实时反推</b>(与 <c>RbacPermissionProvider</c> 走同一条授权链):
/// 用户被授权某模块下任一菜单即"拥有"该模块;超管见全部启用模块。此为门户/登录时调用,非每请求热路径,不缓存。</para>
/// <para>类 public、方法 virtual,可继承覆写(设计 §5.3)。</para>
/// </summary>
public interface IMenuService
{
    /// <summary>当前用户可访问的模块列表(启用的);超管返回全部启用模块。无授权返回空。</summary>
    Task<IReadOnlyList<ModuleItem>> GetMyModulesAsync(long userId, bool isSuperAdmin);

    /// <summary>
    /// 当前用户在指定模块下的侧边栏菜单树(目录/页面,按钮不入导航)。
    /// 非超管按"授权叶子 ∪ 其祖先目录"与该模块节点求交;超管得该模块全树。
    /// </summary>
    Task<IReadOnlyList<MenuNode>> GetMyMenuTreeAsync(long userId, bool isSuperAdmin, long moduleId);
}
