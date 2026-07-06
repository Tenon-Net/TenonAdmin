using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 基础菜单种子(设计 §16,T1)。只播<b>当前后端真实存在的受保护接口</b>对应的节点,
/// 不预埋尚不存在页面的菜单(避免种子超前于实现)。后续模块落地时同批增补自己的菜单节点。
/// <para>权限码必须与 <c>[RolePermission]</c> 授权管道算出的规范化路由一字不差
/// (大写 Method + 冒号 + 小写路由模板),否则授了也匹配不上。</para>
/// </summary>
internal sealed class DefaultMenuSeed : ISeedData<SysMenu>
{
    public IEnumerable<SysMenu> HasData() =>
    [
        // 顶级目录:系统管理(仅分组,不带权限码)
        new SysMenu { Id = 1, ParentId = 0, Type = MenuType.Catalog, Title = "系统管理", Permission = "", Sort = 1, Enabled = true },

        // 探针接口(PingController: GET /api/v1/ping)——常驻冒烟点,也是最小可授权码样例
        new SysMenu { Id = 2, ParentId = 1, Type = MenuType.Button, Title = "连通性探针", Permission = "GET:/api/v1/ping", Sort = 1, Enabled = true },

        // 角色授权接口(SysRoleController: PUT /api/v1/sys/role/menu)
        new SysMenu { Id = 3, ParentId = 1, Type = MenuType.Button, Title = "角色授权", Permission = "PUT:/api/v1/sys/role/menu", Sort = 2, Enabled = true },
    ];
}
