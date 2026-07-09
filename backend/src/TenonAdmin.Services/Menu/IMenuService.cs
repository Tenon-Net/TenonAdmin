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

    // ── 管理端 CRUD(菜单管理页,设计 §16)────────────────────────────────
    // 与上面的门户只读方法分属两类:门户按当前用户授权裁剪;管理端返回全量原始菜单树。

    /// <summary>管理端:全量菜单树(目录/页面/按钮三级,含停用节点,全字段)。</summary>
    Task<IReadOnlyList<MenuTreeNode>> GetTreeAsync();

    /// <summary>管理端:新增菜单,返回新 Id。<c>ModuleId</c> 仅顶级目录有效。</summary>
    Task<long> CreateAsync(MenuInput input);

    /// <summary>管理端:更新菜单;不存在抛 <c>MenuNotFound</c>。</summary>
    Task UpdateAsync(long id, MenuInput input);

    /// <summary>管理端:删除菜单(软删);带子节点抛 <c>MenuHasChildren</c>。</summary>
    Task DeleteAsync(long id);
}
