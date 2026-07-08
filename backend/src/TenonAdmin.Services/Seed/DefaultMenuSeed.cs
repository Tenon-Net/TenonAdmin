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
        // 顶级目录:系统管理(仅分组,不带权限码)。挂靠内置 system 模块(多应用门户,ModuleId 仅顶级目录设)。
        new SysMenu { Id = 1, ParentId = 0, Type = MenuType.Catalog, Title = "系统管理", Permission = "", Sort = 1, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 探针接口(PingController: GET /api/v1/ping)——常驻冒烟点,也是最小可授权码样例
        new SysMenu { Id = 2, ParentId = 1, Type = MenuType.Button, Title = "连通性探针", Permission = "GET:/api/v1/ping", Sort = 1, Enabled = true },

        // 角色授权接口(SysRoleController: PUT /api/v1/sys/role/menu)
        new SysMenu { Id = 3, ParentId = 1, Type = MenuType.Button, Title = "角色授权", Permission = "PUT:/api/v1/sys/role/menu", Sort = 2, Enabled = true },
        // 角色数据范围(SysRoleController: PUT /api/v1/sys/role/datascope,T3)
        new SysMenu { Id = 4, ParentId = 1, Type = MenuType.Button, Title = "角色数据范围", Permission = "PUT:/api/v1/sys/role/datascope", Sort = 3, Enabled = true },
        // 会话管理(SysSessionController,T4)
        new SysMenu { Id = 5, ParentId = 1, Type = MenuType.Button, Title = "在线会话", Permission = "GET:/api/v1/sys/session/online", Sort = 4, Enabled = true },
        new SysMenu { Id = 6, ParentId = 1, Type = MenuType.Button, Title = "强制下线", Permission = "DELETE:/api/v1/sys/session/{sessionid}", Sort = 5, Enabled = true },
        // 日志查询接口(SysLogController,T6)
        new SysMenu { Id = 7, ParentId = 1, Type = MenuType.Button, Title = "操作日志-分页", Permission = "GET:/api/v1/sys/log/op/page", Sort = 6, Enabled = true },
        new SysMenu { Id = 8, ParentId = 1, Type = MenuType.Button, Title = "登录日志-分页", Permission = "GET:/api/v1/sys/log/login/page", Sort = 7, Enabled = true },

        // 组织管理目录 + 各模块代表性接口(T2)。前端完整菜单树随 M2 落地补齐,这里只播真实存在的接口。
        new SysMenu { Id = 10, ParentId = 0, Type = MenuType.Catalog, Title = "组织管理", Permission = "", Sort = 2, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        // 页面节点(M2 前端动态路由入口):用户管理页,component 对应 web/src/views/system/user/index.vue。
        // 目录仅分组,页面节点才带 Path/Component;权限码留空(导航节点),按钮码由兄弟节点 11 承载。
        new SysMenu { Id = 15, ParentId = 10, Type = MenuType.Menu, Title = "用户管理", Permission = "", Path = "/system/user", Component = "system/user/index", Icon = "ph:users-duotone", Sort = 0, Enabled = true, Visible = true },
        new SysMenu { Id = 11, ParentId = 10, Type = MenuType.Button, Title = "用户-分页", Permission = "GET:/api/v1/sys/user/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 12, ParentId = 10, Type = MenuType.Button, Title = "用户-新增", Permission = "POST:/api/v1/sys/user", Sort = 2, Enabled = true },
        new SysMenu { Id = 13, ParentId = 10, Type = MenuType.Button, Title = "机构-列表", Permission = "GET:/api/v1/sys/org/list", Sort = 3, Enabled = true },
        new SysMenu { Id = 14, ParentId = 10, Type = MenuType.Button, Title = "职位-分页", Permission = "GET:/api/v1/sys/position/page", Sort = 4, Enabled = true },

        // 字典与配置目录 + 代表性接口(T5)。同样只播真实存在的接口,完整菜单树随 M2 补齐。
        new SysMenu { Id = 20, ParentId = 0, Type = MenuType.Catalog, Title = "字典配置", Permission = "", Sort = 3, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        new SysMenu { Id = 21, ParentId = 20, Type = MenuType.Button, Title = "字典类型-分页", Permission = "GET:/api/v1/sys/dict/type/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 22, ParentId = 20, Type = MenuType.Button, Title = "字典项-查询", Permission = "GET:/api/v1/sys/dict/items/{typecode}", Sort = 2, Enabled = true },
        new SysMenu { Id = 23, ParentId = 20, Type = MenuType.Button, Title = "字典项-新增", Permission = "POST:/api/v1/sys/dict/item", Sort = 3, Enabled = true },
        new SysMenu { Id = 24, ParentId = 20, Type = MenuType.Button, Title = "配置-分页", Permission = "GET:/api/v1/sys/config/page", Sort = 4, Enabled = true },
        new SysMenu { Id = 25, ParentId = 20, Type = MenuType.Button, Title = "配置-取值", Permission = "GET:/api/v1/sys/config/value/{key}", Sort = 5, Enabled = true },

        // 文件管理目录 + 代表性接口(T7)。同样只播真实存在的接口。
        new SysMenu { Id = 30, ParentId = 0, Type = MenuType.Catalog, Title = "文件管理", Permission = "", Sort = 4, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        new SysMenu { Id = 31, ParentId = 30, Type = MenuType.Button, Title = "文件-上传", Permission = "POST:/api/v1/sys/file/upload", Sort = 1, Enabled = true },
        new SysMenu { Id = 32, ParentId = 30, Type = MenuType.Button, Title = "文件-分页", Permission = "GET:/api/v1/sys/file/page", Sort = 2, Enabled = true },
    ];
}
