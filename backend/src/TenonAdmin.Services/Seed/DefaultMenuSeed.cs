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
        new SysMenu { Id = 1, ParentId = 0, Type = MenuType.Catalog, Title = "系统管理", Permission = "", Icon = "ph:gear-duotone", Sort = 1, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

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

        // 菜单管理页(M2:MenuController CRUD)。页面节点带 Path/Component;四个操作码由兄弟按钮承载。
        new SysMenu { Id = 40, ParentId = 1, Type = MenuType.Menu, Title = "菜单管理", Permission = "", Path = "/system/menu", Component = "system/menu/index", Icon = "ph:list-dashes-duotone", Sort = 8, Enabled = true, Visible = true },
        new SysMenu { Id = 41, ParentId = 1, Type = MenuType.Button, Title = "菜单-树", Permission = "GET:/api/v1/sys/menu/tree", Sort = 9, Enabled = true },
        new SysMenu { Id = 42, ParentId = 1, Type = MenuType.Button, Title = "菜单-新增", Permission = "POST:/api/v1/sys/menu/add", Sort = 10, Enabled = true },
        new SysMenu { Id = 43, ParentId = 1, Type = MenuType.Button, Title = "菜单-更新", Permission = "PUT:/api/v1/sys/menu/{id}", Sort = 11, Enabled = true },
        new SysMenu { Id = 44, ParentId = 1, Type = MenuType.Button, Title = "菜单-删除", Permission = "DELETE:/api/v1/sys/menu/{id}", Sort = 12, Enabled = true },

        // 模块管理页(M2:ModuleController CRUD,M1.5 已建接口,前端此番补页 + 补授权码节点)。
        new SysMenu { Id = 45, ParentId = 1, Type = MenuType.Menu, Title = "模块管理", Permission = "", Path = "/system/module", Component = "system/module/index", Icon = "ph:squares-four-duotone", Sort = 13, Enabled = true, Visible = true },
        new SysMenu { Id = 46, ParentId = 1, Type = MenuType.Button, Title = "模块-列表", Permission = "GET:/api/v1/sys/module/list", Sort = 14, Enabled = true },
        new SysMenu { Id = 47, ParentId = 1, Type = MenuType.Button, Title = "模块-新增", Permission = "POST:/api/v1/sys/module/add", Sort = 15, Enabled = true },
        new SysMenu { Id = 48, ParentId = 1, Type = MenuType.Button, Title = "模块-更新", Permission = "PUT:/api/v1/sys/module/{id}", Sort = 16, Enabled = true },
        new SysMenu { Id = 49, ParentId = 1, Type = MenuType.Button, Title = "模块-删除", Permission = "DELETE:/api/v1/sys/module/{id}", Sort = 17, Enabled = true },

        // 操作日志页(R3:SysLogController 只读 + 详情 + 清空)。分页码由已有按钮 7 承载,此处补页节点 + 清空码。
        new SysMenu { Id = 66, ParentId = 1, Type = MenuType.Menu, Title = "操作日志", Permission = "", Path = "/system/log/op", Component = "system/log/op/index", Icon = "ph:scroll-duotone", Sort = 18, Enabled = true, Visible = true },
        new SysMenu { Id = 67, ParentId = 1, Type = MenuType.Button, Title = "操作日志-清空", Permission = "DELETE:/api/v1/sys/log/op", Sort = 19, Enabled = true },

        // 登录日志页(R2:SysLogController 只读 + 清空)。分页码由已有按钮 8 承载,此处补页节点 + 清空码。
        new SysMenu { Id = 68, ParentId = 1, Type = MenuType.Menu, Title = "登录日志", Permission = "", Path = "/system/log/login", Component = "system/log/login/index", Icon = "ph:sign-in-duotone", Sort = 20, Enabled = true, Visible = true },
        new SysMenu { Id = 69, ParentId = 1, Type = MenuType.Button, Title = "登录日志-清空", Permission = "DELETE:/api/v1/sys/log/login", Sort = 21, Enabled = true },

        // 组织管理目录 + 各模块代表性接口(T2)。前端完整菜单树随 M2 落地补齐,这里只播真实存在的接口。
        new SysMenu { Id = 10, ParentId = 0, Type = MenuType.Catalog, Title = "组织管理", Permission = "", Icon = "ph:buildings-duotone", Sort = 2, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        // 页面节点(M2 前端动态路由入口):用户管理页,component 对应 web/src/views/system/user/index.vue。
        // 目录仅分组,页面节点才带 Path/Component;权限码留空(导航节点),按钮码由兄弟节点 11 承载。
        new SysMenu { Id = 15, ParentId = 10, Type = MenuType.Menu, Title = "用户管理", Permission = "", Path = "/system/user", Component = "system/user/index", Icon = "ph:users-duotone", Sort = 0, Enabled = true, Visible = true },
        new SysMenu { Id = 11, ParentId = 10, Type = MenuType.Button, Title = "用户-分页", Permission = "GET:/api/v1/sys/user/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 12, ParentId = 10, Type = MenuType.Button, Title = "用户-新增", Permission = "POST:/api/v1/sys/user", Sort = 2, Enabled = true },
        new SysMenu { Id = 13, ParentId = 10, Type = MenuType.Button, Title = "机构-列表", Permission = "GET:/api/v1/sys/org/list", Sort = 3, Enabled = true },
        new SysMenu { Id = 14, ParentId = 10, Type = MenuType.Button, Title = "职位-分页", Permission = "GET:/api/v1/sys/position/page", Sort = 4, Enabled = true },
        // 用户写侧授权码(R4:UserController;新增 POST 已 Id 12)。页节点 15 已在。
        new SysMenu { Id = 50, ParentId = 10, Type = MenuType.Button, Title = "用户-详情", Permission = "GET:/api/v1/sys/user/{id}", Sort = 5, Enabled = true },
        new SysMenu { Id = 51, ParentId = 10, Type = MenuType.Button, Title = "用户-更新", Permission = "PUT:/api/v1/sys/user/{id}", Sort = 6, Enabled = true },
        new SysMenu { Id = 52, ParentId = 10, Type = MenuType.Button, Title = "用户-删除", Permission = "DELETE:/api/v1/sys/user/{id}", Sort = 7, Enabled = true },
        new SysMenu { Id = 53, ParentId = 10, Type = MenuType.Button, Title = "用户-重置密码", Permission = "PUT:/api/v1/sys/user/{id}/password", Sort = 8, Enabled = true },
        new SysMenu { Id = 54, ParentId = 10, Type = MenuType.Button, Title = "用户-启停", Permission = "PUT:/api/v1/sys/user/{id}/enabled", Sort = 9, Enabled = true },

        // 岗位管理页(R6:PositionController 普通 CRUD)。分页码由已有按钮 14 承载;写端点授权码由 75-77 承载;
        // 详情 GET position/{id} 不放按钮(编辑用行数据)。
        new SysMenu { Id = 74, ParentId = 10, Type = MenuType.Menu, Title = "岗位管理", Permission = "", Path = "/system/position", Component = "system/position/index", Icon = "ph:identification-badge-duotone", Sort = 10, Enabled = true, Visible = true },
        new SysMenu { Id = 75, ParentId = 10, Type = MenuType.Button, Title = "岗位-新增", Permission = "POST:/api/v1/sys/position/add", Sort = 11, Enabled = true },
        new SysMenu { Id = 76, ParentId = 10, Type = MenuType.Button, Title = "岗位-更新", Permission = "PUT:/api/v1/sys/position/{id}", Sort = 12, Enabled = true },
        new SysMenu { Id = 77, ParentId = 10, Type = MenuType.Button, Title = "岗位-删除", Permission = "DELETE:/api/v1/sys/position/{id}", Sort = 13, Enabled = true },

        // 字典与配置目录 + 代表性接口(T5)。同样只播真实存在的接口,完整菜单树随 M2 补齐。
        new SysMenu { Id = 20, ParentId = 0, Type = MenuType.Catalog, Title = "字典配置", Permission = "", Icon = "ph:book-bookmark-duotone", Sort = 3, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        new SysMenu { Id = 21, ParentId = 20, Type = MenuType.Button, Title = "字典类型-分页", Permission = "GET:/api/v1/sys/dict/type/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 22, ParentId = 20, Type = MenuType.Button, Title = "字典项-查询", Permission = "GET:/api/v1/sys/dict/items/{typecode}", Sort = 2, Enabled = true },
        new SysMenu { Id = 23, ParentId = 20, Type = MenuType.Button, Title = "字典项-新增", Permission = "POST:/api/v1/sys/dict/item", Sort = 3, Enabled = true },
        new SysMenu { Id = 24, ParentId = 20, Type = MenuType.Button, Title = "配置-分页", Permission = "GET:/api/v1/sys/config/page", Sort = 4, Enabled = true },
        new SysMenu { Id = 25, ParentId = 20, Type = MenuType.Button, Title = "配置-取值", Permission = "GET:/api/v1/sys/config/value/{key}", Sort = 5, Enabled = true },
        // 配置管理页(R1:ConfigController CRUD)。页面节点带 Path/Component;写端点授权码由兄弟按钮 57-59 承载(detail 用行数据,不放 GET config/{id})。
        new SysMenu { Id = 55, ParentId = 20, Type = MenuType.Menu, Title = "配置管理", Permission = "", Path = "/system/config", Component = "system/config/index", Icon = "ph:sliders-horizontal-duotone", Sort = 6, Enabled = true, Visible = true },
        new SysMenu { Id = 57, ParentId = 20, Type = MenuType.Button, Title = "配置-新增", Permission = "POST:/api/v1/sys/config", Sort = 7, Enabled = true },
        new SysMenu { Id = 58, ParentId = 20, Type = MenuType.Button, Title = "配置-更新", Permission = "PUT:/api/v1/sys/config/{id}", Sort = 8, Enabled = true },
        new SysMenu { Id = 59, ParentId = 20, Type = MenuType.Button, Title = "配置-删除", Permission = "DELETE:/api/v1/sys/config/{id}", Sort = 9, Enabled = true },
        // 字典管理页(R5:DictController 主从 CRUD)。写端点授权码由兄弟按钮 61-65 承载;
        // 82 是管理端项分页(含停用,R5 新增端点);类型详情 GET dict/type/{id} 不放按钮(编辑用行数据)。
        new SysMenu { Id = 60, ParentId = 20, Type = MenuType.Menu, Title = "字典管理", Permission = "", Path = "/system/dict", Component = "system/dict/index", Icon = "ph:book-open-text-duotone", Sort = 10, Enabled = true, Visible = true },
        new SysMenu { Id = 61, ParentId = 20, Type = MenuType.Button, Title = "字典类型-新增", Permission = "POST:/api/v1/sys/dict/type", Sort = 11, Enabled = true },
        new SysMenu { Id = 62, ParentId = 20, Type = MenuType.Button, Title = "字典类型-更新", Permission = "PUT:/api/v1/sys/dict/type/{id}", Sort = 12, Enabled = true },
        new SysMenu { Id = 63, ParentId = 20, Type = MenuType.Button, Title = "字典类型-删除", Permission = "DELETE:/api/v1/sys/dict/type/{id}", Sort = 13, Enabled = true },
        new SysMenu { Id = 64, ParentId = 20, Type = MenuType.Button, Title = "字典项-更新", Permission = "PUT:/api/v1/sys/dict/item/{id}", Sort = 14, Enabled = true },
        new SysMenu { Id = 65, ParentId = 20, Type = MenuType.Button, Title = "字典项-删除", Permission = "DELETE:/api/v1/sys/dict/item/{id}", Sort = 15, Enabled = true },
        new SysMenu { Id = 82, ParentId = 20, Type = MenuType.Button, Title = "字典项-分页", Permission = "GET:/api/v1/sys/dict/item/page", Sort = 16, Enabled = true },

        // 文件管理目录 + 代表性接口(T7)。同样只播真实存在的接口。
        new SysMenu { Id = 30, ParentId = 0, Type = MenuType.Catalog, Title = "文件管理", Permission = "", Icon = "ph:folder-duotone", Sort = 4, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        new SysMenu { Id = 31, ParentId = 30, Type = MenuType.Button, Title = "文件-上传", Permission = "POST:/api/v1/sys/file/upload", Sort = 1, Enabled = true },
        new SysMenu { Id = 32, ParentId = 30, Type = MenuType.Button, Title = "文件-分页", Permission = "GET:/api/v1/sys/file/page", Sort = 2, Enabled = true },
    ];
}
