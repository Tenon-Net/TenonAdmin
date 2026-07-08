using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IMenuService"/> 默认实现。菜单表很小,整表载入内存做树运算(上溯根目录取 ModuleId、
/// 授权叶子的祖先脚手架),避免多次递归查库。授权链复用 <c>RbacPermissionProvider</c> 同款三步短路。
/// </summary>
public class MenuService(
    IRepository<SysUserRole> userRoles,
    IRepository<SysRoleMenu> roleMenus,
    IRepository<SysMenu> menus,
    IRepository<SysModule> modules) : IMenuService
{
    /// <summary>上溯 ParentId 链到根目录的最大步数(防断链/环)。菜单层级远小于此。</summary>
    private const int WalkGuard = 64;

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<ModuleItem>> GetMyModulesAsync(long userId, bool isSuperAdmin)
    {
        var allModules = await modules.AsQueryable().Where(m => m.Enabled).OrderBy(m => m.Sort).OrderBy(m => m.Id).ToListAsync();
        if (isSuperAdmin) return allModules.Select(ToItem).ToList();

        var roleIds = await userRoles.AsQueryable().Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync();
        if (roleIds.Count == 0) return [];
        var grantedMenuIds = await roleMenus.AsQueryable().Where(x => roleIds.Contains(x.RoleId)).Select(x => x.MenuId).ToListAsync();
        if (grantedMenuIds.Count == 0) return [];

        // 只用启用菜单反推模块访问权:与 RbacPermissionProvider 的生效权限口径一致。
        // 若角色仍关联已停用菜单,它不应继续让用户在门户看到该模块(Permission 本身也不会授出)。
        var byId = (await menus.AsQueryable().Where(m => m.Enabled).ToListAsync()).ToDictionary(m => m.Id);
        var accessibleModuleIds = grantedMenuIds
            .Select(id => RootModuleId(id, byId))
            .Where(mid => mid is not null)
            .Select(mid => mid!.Value)
            .ToHashSet();

        return allModules.Where(m => accessibleModuleIds.Contains(m.Id)).Select(ToItem).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<MenuNode>> GetMyMenuTreeAsync(long userId, bool isSuperAdmin, long moduleId)
    {
        var allMenus = await menus.AsQueryable().Where(m => m.Enabled).OrderBy(m => m.Sort).OrderBy(m => m.Id).ToListAsync();
        var byId = allMenus.ToDictionary(m => m.Id);

        // 该模块下的节点 = 其根目录 ModuleId == moduleId
        var moduleMenus = allMenus.Where(m => RootModuleId(m.Id, byId) == moduleId).ToList();

        IEnumerable<SysMenu> visible = moduleMenus;
        if (!isSuperAdmin)
        {
            var roleIds = await userRoles.AsQueryable().Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync();
            var grantedMenuIds = roleIds.Count == 0
                ? []
                : await roleMenus.AsQueryable().Where(x => roleIds.Contains(x.RoleId)).Select(x => x.MenuId).ToListAsync();

            // 授权叶子 ∪ 其祖先目录(脚手架:让被授权节点在树上有完整的父路径)
            var keep = new HashSet<long>();
            foreach (var id in grantedMenuIds)
            {
                var cur = byId.GetValueOrDefault(id);
                var guard = 0;
                while (cur is not null && guard++ < WalkGuard)
                {
                    if (!keep.Add(cur.Id)) break;          // 已收录,其祖先链亦已收录
                    if (cur.ParentId == 0) break;
                    cur = byId.GetValueOrDefault(cur.ParentId);
                }
            }
            visible = moduleMenus.Where(m => keep.Contains(m.Id));
        }

        // 只保留导航节点(目录/页面);按钮仅承载权限码,不进侧边栏。
        var navNodes = visible.Where(m => m.Type != MenuType.Button).ToList();
        return BuildForest(navNodes);
    }

    /// <summary>上溯 <paramref name="menuId"/> 的 ParentId 链到根目录,返回根目录的 ModuleId(未挂模块或断链为 null)。</summary>
    private static long? RootModuleId(long menuId, IReadOnlyDictionary<long, SysMenu> byId)
    {
        var cur = byId.GetValueOrDefault(menuId);
        var guard = 0;
        while (cur is not null && cur.ParentId != 0 && guard++ < WalkGuard)
        {
            if (!byId.TryGetValue(cur.ParentId, out var parent)) break;
            cur = parent;
        }
        return cur?.ModuleId;
    }

    /// <summary>按 ParentId 把平铺(已排序)节点拼成森林;父不在集合内的节点升为根。</summary>
    private static IReadOnlyList<MenuNode> BuildForest(List<SysMenu> nodes)
    {
        var nodeMap = nodes.ToDictionary(m => m.Id, ToNode);
        var roots = new List<MenuNode>();
        foreach (var m in nodes)
        {
            var node = nodeMap[m.Id];
            if (m.ParentId != 0 && nodeMap.TryGetValue(m.ParentId, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }
        return roots;
    }

    private static ModuleItem ToItem(SysModule m) => new()
    {
        Id = m.Id, Code = m.Code, Title = m.Title, Icon = m.Icon, DefaultRoute = m.DefaultRoute, Sort = m.Sort,
    };

    private static MenuNode ToNode(SysMenu m) => new()
    {
        Id = m.Id, ParentId = m.ParentId, Type = m.Type, Title = m.Title,
        Path = m.Path, Component = m.Component, Icon = m.Icon, Sort = m.Sort, Visible = m.Visible,
    };
}
