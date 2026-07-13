using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 基础菜单种子(设计 §16,T1)。只播<b>当前后端真实存在的受保护接口</b>对应的节点,
/// 不预埋尚不存在页面的菜单(避免种子超前于实现)。后续模块落地时同批增补自己的菜单节点。
/// <para>权限码必须与 <c>[RolePermission]</c> 授权管道算出的规范化路由一字不差
/// (大写 Method + 冒号 + 小写路由模板),否则授了也匹配不上。</para>
/// <para>菜单树顶级节点:system 模块下工作台(根级页面)+ 组织管理 / 系统运维 / 日志审计 / 文件管理四个目录;
/// 另有示例 business 模块下一条工作台(ModuleId 仅顶级节点设)。节点 Id 手工分配、无分配器,新增节点接着当前
/// 最大值往后取(现已用到 109),避免覆盖历史 Id。</para>
/// </summary>
internal sealed class DefaultMenuSeed : ISeedData<SysMenu>
{
    public IEnumerable<SysMenu> HasData() =>
    [
        // ═══ 工作台 ═════════════════════════════════════════════════
        // 每个应用有自己的首页:工作台是一条普通菜单(根级 Menu 节点,故可挂 ModuleId),不是全局静态页。
        // Sort=0 → 排在所有目录前,也让它成为菜单树首个叶子(前端 homePath() 的兜底落点)。
        // 该页不打任何后端接口,故 Permission 为空。
        new SysMenu { Id = 108, ParentId = 0, Type = MenuType.Menu, Title = "工作台", Permission = "", Path = "/workbench", Component = "dashboard/workbench", Icon = "ph:squares-four-duotone", Sort = 0, Enabled = true, Visible = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // ═══ 组织管理 ═══════════════════════════════════════════════
        new SysMenu { Id = 10, ParentId = 0, Type = MenuType.Catalog, Title = "组织管理", Permission = "", Icon = "ph:buildings-duotone", Sort = 1, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 机构管理页(R9:OrgController 树 CRUD)。列表码 13、写端点码 71-73;详情 GET org/{id} 不放按钮(编辑用行数据)。
        new SysMenu { Id = 70, ParentId = 10, Type = MenuType.Menu, Title = "机构管理", Permission = "", Path = "/system/org", Component = "system/org/index", Icon = "ph:tree-structure-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 13, ParentId = 10, Type = MenuType.Button, Title = "机构-列表", Permission = "GET:/api/v1/sys/org/list", Sort = 1, Enabled = true },
        new SysMenu { Id = 71, ParentId = 10, Type = MenuType.Button, Title = "机构-新增", Permission = "POST:/api/v1/sys/org/add", Sort = 2, Enabled = true },
        new SysMenu { Id = 72, ParentId = 10, Type = MenuType.Button, Title = "机构-更新", Permission = "PUT:/api/v1/sys/org/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 73, ParentId = 10, Type = MenuType.Button, Title = "机构-删除", Permission = "DELETE:/api/v1/sys/org/{id}", Sort = 4, Enabled = true },
        new SysMenu { Id = 98, ParentId = 10, Type = MenuType.Button, Title = "机构-复制", Permission = "POST:/api/v1/sys/org/{id}/copy", Sort = 29, Enabled = true },

        // 岗位管理页(R6:PositionController 普通 CRUD)。分页码 14、写端点码 75-77;详情 GET position/{id} 不放按钮。
        new SysMenu { Id = 74, ParentId = 10, Type = MenuType.Menu, Title = "岗位管理", Permission = "", Path = "/system/position", Component = "system/position/index", Icon = "ph:identification-badge-duotone", Sort = 2, Enabled = true, Visible = true },
        new SysMenu { Id = 14, ParentId = 10, Type = MenuType.Button, Title = "职位-分页", Permission = "GET:/api/v1/sys/position/page", Sort = 5, Enabled = true },
        new SysMenu { Id = 75, ParentId = 10, Type = MenuType.Button, Title = "岗位-新增", Permission = "POST:/api/v1/sys/position/add", Sort = 6, Enabled = true },
        new SysMenu { Id = 76, ParentId = 10, Type = MenuType.Button, Title = "岗位-更新", Permission = "PUT:/api/v1/sys/position/{id}", Sort = 7, Enabled = true },
        new SysMenu { Id = 77, ParentId = 10, Type = MenuType.Button, Title = "岗位-删除", Permission = "DELETE:/api/v1/sys/position/{id}", Sort = 8, Enabled = true },
        new SysMenu { Id = 99, ParentId = 10, Type = MenuType.Button, Title = "岗位-拖拽排序", Permission = "POST:/api/v1/sys/position/reorder", Sort = 9, Enabled = true },

        // 用户管理页(R4:UserController)。分页码 11、新增 12;写侧码 50-54。
        new SysMenu { Id = 15, ParentId = 10, Type = MenuType.Menu, Title = "用户管理", Permission = "", Path = "/system/user", Component = "system/user/index", Icon = "ph:users-duotone", Sort = 3, Enabled = true, Visible = true },
        new SysMenu { Id = 11, ParentId = 10, Type = MenuType.Button, Title = "用户-分页", Permission = "GET:/api/v1/sys/user/page", Sort = 9, Enabled = true },
        new SysMenu { Id = 12, ParentId = 10, Type = MenuType.Button, Title = "用户-新增", Permission = "POST:/api/v1/sys/user", Sort = 10, Enabled = true },
        new SysMenu { Id = 50, ParentId = 10, Type = MenuType.Button, Title = "用户-详情", Permission = "GET:/api/v1/sys/user/{id}", Sort = 11, Enabled = true },
        new SysMenu { Id = 51, ParentId = 10, Type = MenuType.Button, Title = "用户-更新", Permission = "PUT:/api/v1/sys/user/{id}", Sort = 12, Enabled = true },
        new SysMenu { Id = 52, ParentId = 10, Type = MenuType.Button, Title = "用户-删除", Permission = "DELETE:/api/v1/sys/user/{id}", Sort = 13, Enabled = true },
        new SysMenu { Id = 83, ParentId = 10, Type = MenuType.Button, Title = "用户-批量删除", Permission = "POST:/api/v1/sys/user/batch-delete", Sort = 16, Enabled = true },
        new SysMenu { Id = 53, ParentId = 10, Type = MenuType.Button, Title = "用户-重置密码", Permission = "PUT:/api/v1/sys/user/{id}/password", Sort = 14, Enabled = true },
        new SysMenu { Id = 54, ParentId = 10, Type = MenuType.Button, Title = "用户-启停", Permission = "PUT:/api/v1/sys/user/{id}/enabled", Sort = 15, Enabled = true },

        // 角色管理页(SysRoleController:CRUD + 授菜单权限 + 配数据范围)。
        // 授权抽屉读菜单树复用系统运维「菜单-树」锚点(Id=41);授菜单/数据范围锚点 Id 3/4 从系统运维挪到此组。
        new SysMenu { Id = 87, ParentId = 10, Type = MenuType.Menu, Title = "角色管理", Permission = "", Path = "/system/role", Component = "system/role/index", Icon = "ph:shield-check-duotone", Sort = 4, Enabled = true, Visible = true },
        new SysMenu { Id = 88, ParentId = 10, Type = MenuType.Button, Title = "角色-分页", Permission = "GET:/api/v1/sys/role/page", Sort = 17, Enabled = true },
        new SysMenu { Id = 89, ParentId = 10, Type = MenuType.Button, Title = "角色-详情", Permission = "GET:/api/v1/sys/role/{id}", Sort = 18, Enabled = true },
        new SysMenu { Id = 91, ParentId = 10, Type = MenuType.Button, Title = "角色-新增", Permission = "POST:/api/v1/sys/role/add", Sort = 19, Enabled = true },
        new SysMenu { Id = 92, ParentId = 10, Type = MenuType.Button, Title = "角色-更新", Permission = "PUT:/api/v1/sys/role/{id}", Sort = 20, Enabled = true },
        new SysMenu { Id = 93, ParentId = 10, Type = MenuType.Button, Title = "角色-删除", Permission = "DELETE:/api/v1/sys/role/{id}", Sort = 21, Enabled = true },
        new SysMenu { Id = 94, ParentId = 10, Type = MenuType.Button, Title = "角色-批量删除", Permission = "POST:/api/v1/sys/role/batch-delete", Sort = 22, Enabled = true },
        new SysMenu { Id = 95, ParentId = 10, Type = MenuType.Button, Title = "角色-取菜单", Permission = "GET:/api/v1/sys/role/{id}/menus", Sort = 23, Enabled = true },
        new SysMenu { Id = 96, ParentId = 10, Type = MenuType.Button, Title = "角色-取数据范围", Permission = "GET:/api/v1/sys/role/{id}/datascope", Sort = 24, Enabled = true },
        new SysMenu { Id = 3, ParentId = 10, Type = MenuType.Button, Title = "角色-授权菜单", Permission = "PUT:/api/v1/sys/role/menu", Sort = 25, Enabled = true },
        new SysMenu { Id = 4, ParentId = 10, Type = MenuType.Button, Title = "角色-数据范围", Permission = "PUT:/api/v1/sys/role/datascope", Sort = 26, Enabled = true },

        // ═══ 系统运维 ═══════════════════════════════════════════════
        new SysMenu { Id = 20, ParentId = 0, Type = MenuType.Catalog, Title = "系统运维", Permission = "", Icon = "ph:wrench-duotone", Sort = 2, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 系统配置页(R1:ConfigController CRUD)。分页码 24、取值 25;写端点码 57-59;详情 GET config/{id} 不放按钮。
        new SysMenu { Id = 55, ParentId = 20, Type = MenuType.Menu, Title = "系统配置", Permission = "", Path = "/system/config", Component = "system/config/index", Icon = "ph:sliders-horizontal-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 24, ParentId = 20, Type = MenuType.Button, Title = "配置-分页", Permission = "GET:/api/v1/sys/config/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 25, ParentId = 20, Type = MenuType.Button, Title = "配置-取值", Permission = "GET:/api/v1/sys/config/value/{key}", Sort = 2, Enabled = true },
        new SysMenu { Id = 57, ParentId = 20, Type = MenuType.Button, Title = "配置-新增", Permission = "POST:/api/v1/sys/config", Sort = 3, Enabled = true },
        new SysMenu { Id = 58, ParentId = 20, Type = MenuType.Button, Title = "配置-更新", Permission = "PUT:/api/v1/sys/config/{id}", Sort = 4, Enabled = true },
        new SysMenu { Id = 59, ParentId = 20, Type = MenuType.Button, Title = "配置-删除", Permission = "DELETE:/api/v1/sys/config/{id}", Sort = 5, Enabled = true },
        new SysMenu { Id = 97, ParentId = 20, Type = MenuType.Button, Title = "配置-批量存值", Permission = "PUT:/api/v1/sys/config/batch", Sort = 28, Enabled = true },

        // 字典管理页(R5:DictController 主从 CRUD)。类型分页 21、项查询 22、项新增 23;写端点码 61-65;82 管理端项分页。
        new SysMenu { Id = 60, ParentId = 20, Type = MenuType.Menu, Title = "字典管理", Permission = "", Path = "/system/dict", Component = "system/dict/index", Icon = "ph:book-open-text-duotone", Sort = 2, Enabled = true, Visible = true },
        new SysMenu { Id = 21, ParentId = 20, Type = MenuType.Button, Title = "字典类型-分页", Permission = "GET:/api/v1/sys/dict/type/page", Sort = 6, Enabled = true },
        new SysMenu { Id = 22, ParentId = 20, Type = MenuType.Button, Title = "字典项-查询", Permission = "GET:/api/v1/sys/dict/items/{typecode}", Sort = 7, Enabled = true },
        new SysMenu { Id = 23, ParentId = 20, Type = MenuType.Button, Title = "字典项-新增", Permission = "POST:/api/v1/sys/dict/item", Sort = 8, Enabled = true },
        new SysMenu { Id = 61, ParentId = 20, Type = MenuType.Button, Title = "字典类型-新增", Permission = "POST:/api/v1/sys/dict/type", Sort = 9, Enabled = true },
        new SysMenu { Id = 62, ParentId = 20, Type = MenuType.Button, Title = "字典类型-更新", Permission = "PUT:/api/v1/sys/dict/type/{id}", Sort = 10, Enabled = true },
        new SysMenu { Id = 63, ParentId = 20, Type = MenuType.Button, Title = "字典类型-删除", Permission = "DELETE:/api/v1/sys/dict/type/{id}", Sort = 11, Enabled = true },
        new SysMenu { Id = 64, ParentId = 20, Type = MenuType.Button, Title = "字典项-更新", Permission = "PUT:/api/v1/sys/dict/item/{id}", Sort = 12, Enabled = true },
        new SysMenu { Id = 65, ParentId = 20, Type = MenuType.Button, Title = "字典项-删除", Permission = "DELETE:/api/v1/sys/dict/item/{id}", Sort = 13, Enabled = true },
        new SysMenu { Id = 82, ParentId = 20, Type = MenuType.Button, Title = "字典项-分页", Permission = "GET:/api/v1/sys/dict/item/page", Sort = 14, Enabled = true },
        new SysMenu { Id = 84, ParentId = 20, Type = MenuType.Button, Title = "字典类型-批量删除", Permission = "POST:/api/v1/sys/dict/type/batch-delete", Sort = 26, Enabled = true },
        new SysMenu { Id = 85, ParentId = 20, Type = MenuType.Button, Title = "字典项-批量删除", Permission = "POST:/api/v1/sys/dict/item/batch-delete", Sort = 27, Enabled = true },

        // 菜单管理页(M2:MenuController CRUD)。
        new SysMenu { Id = 40, ParentId = 20, Type = MenuType.Menu, Title = "菜单管理", Permission = "", Path = "/system/menu", Component = "system/menu/index", Icon = "ph:list-dashes-duotone", Sort = 3, Enabled = true, Visible = true },
        new SysMenu { Id = 41, ParentId = 20, Type = MenuType.Button, Title = "菜单-树", Permission = "GET:/api/v1/sys/menu/tree", Sort = 15, Enabled = true },
        new SysMenu { Id = 42, ParentId = 20, Type = MenuType.Button, Title = "菜单-新增", Permission = "POST:/api/v1/sys/menu/add", Sort = 16, Enabled = true },
        new SysMenu { Id = 43, ParentId = 20, Type = MenuType.Button, Title = "菜单-更新", Permission = "PUT:/api/v1/sys/menu/{id}", Sort = 17, Enabled = true },
        new SysMenu { Id = 44, ParentId = 20, Type = MenuType.Button, Title = "菜单-删除", Permission = "DELETE:/api/v1/sys/menu/{id}", Sort = 18, Enabled = true },
        // 路由清单:菜单表单里"权限码"下拉的数据源(不给它就只能手敲权限码,错一个字符即静默 403)。
        new SysMenu { Id = 107, ParentId = 20, Type = MenuType.Button, Title = "菜单-路由清单", Permission = "GET:/api/v1/sys/menu/routes", Sort = 19, Enabled = true },

        // 模块管理页(M2:ModuleController CRUD)。
        new SysMenu { Id = 45, ParentId = 20, Type = MenuType.Menu, Title = "模块管理", Permission = "", Path = "/system/module", Component = "system/module/index", Icon = "ph:squares-four-duotone", Sort = 4, Enabled = true, Visible = true },
        new SysMenu { Id = 46, ParentId = 20, Type = MenuType.Button, Title = "模块-列表", Permission = "GET:/api/v1/sys/module/list", Sort = 19, Enabled = true },
        new SysMenu { Id = 47, ParentId = 20, Type = MenuType.Button, Title = "模块-新增", Permission = "POST:/api/v1/sys/module/add", Sort = 20, Enabled = true },
        new SysMenu { Id = 48, ParentId = 20, Type = MenuType.Button, Title = "模块-更新", Permission = "PUT:/api/v1/sys/module/{id}", Sort = 21, Enabled = true },
        new SysMenu { Id = 49, ParentId = 20, Type = MenuType.Button, Title = "模块-删除", Permission = "DELETE:/api/v1/sys/module/{id}", Sort = 22, Enabled = true },

        // 消息通知页(NoticeController:管理端发布/列表/删除)。用户端(我的/未读数/标记已读)走 [ActiveSession] 无需权限码,故不设按钮。
        new SysMenu { Id = 100, ParentId = 20, Type = MenuType.Menu, Title = "消息通知", Permission = "", Path = "/system/notice", Component = "system/notice/index", Icon = "ph:bell-duotone", Sort = 5, Enabled = true, Visible = true },
        new SysMenu { Id = 101, ParentId = 20, Type = MenuType.Button, Title = "通知-发布", Permission = "POST:/api/v1/sys/notice", Sort = 30, Enabled = true },
        new SysMenu { Id = 102, ParentId = 20, Type = MenuType.Button, Title = "通知-分页", Permission = "GET:/api/v1/sys/notice/page", Sort = 31, Enabled = true },
        new SysMenu { Id = 103, ParentId = 20, Type = MenuType.Button, Title = "通知-删除", Permission = "DELETE:/api/v1/sys/notice/{id}", Sort = 32, Enabled = true },

        // 权限码锚点(暂无独立页面):探针。挂靠系统运维目录,仅承载权限码。
        // (角色授权/数据范围锚点 Id 3/4 已挪到组织管理的角色管理页组,见上。)
        new SysMenu { Id = 2, ParentId = 20, Type = MenuType.Button, Title = "连通性探针", Permission = "GET:/api/v1/ping", Sort = 23, Enabled = true },

        // ═══ 日志审计 ═══════════════════════════════════════════════
        new SysMenu { Id = 90, ParentId = 0, Type = MenuType.Catalog, Title = "日志审计", Permission = "", Icon = "ph:clipboard-text-duotone", Sort = 3, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 登录日志页(R2:SysLogController 只读 + 清空)。分页码 8、清空码 69。
        new SysMenu { Id = 68, ParentId = 90, Type = MenuType.Menu, Title = "登录日志", Permission = "", Path = "/system/log/login", Component = "system/log/login/index", Icon = "ph:sign-in-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 8, ParentId = 90, Type = MenuType.Button, Title = "登录日志-分页", Permission = "GET:/api/v1/sys/log/login/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 69, ParentId = 90, Type = MenuType.Button, Title = "登录日志-清空", Permission = "DELETE:/api/v1/sys/log/login", Sort = 2, Enabled = true },

        // 操作日志页(R3:SysLogController 只读 + 详情 + 清空)。分页码 7、清空码 67。
        new SysMenu { Id = 66, ParentId = 90, Type = MenuType.Menu, Title = "操作日志", Permission = "", Path = "/system/log/op", Component = "system/log/op/index", Icon = "ph:scroll-duotone", Sort = 2, Enabled = true, Visible = true },
        new SysMenu { Id = 7, ParentId = 90, Type = MenuType.Button, Title = "操作日志-分页", Permission = "GET:/api/v1/sys/log/op/page", Sort = 3, Enabled = true },
        new SysMenu { Id = 67, ParentId = 90, Type = MenuType.Button, Title = "操作日志-清空", Permission = "DELETE:/api/v1/sys/log/op", Sort = 4, Enabled = true },

        // 在线会话页(R7:SysSessionController 只读 + 强退)。在线列表码 5、强退码 6。
        new SysMenu { Id = 81, ParentId = 90, Type = MenuType.Menu, Title = "在线会话", Permission = "", Path = "/system/session", Component = "system/session/index", Icon = "ph:broadcast-duotone", Sort = 3, Enabled = true, Visible = true },
        new SysMenu { Id = 5, ParentId = 90, Type = MenuType.Button, Title = "在线会话-列表", Permission = "GET:/api/v1/sys/session/online", Sort = 5, Enabled = true },
        new SysMenu { Id = 6, ParentId = 90, Type = MenuType.Button, Title = "强制下线", Permission = "DELETE:/api/v1/sys/session/{sessionid}", Sort = 6, Enabled = true },

        // ═══ 文件管理 ═══════════════════════════════════════════════
        new SysMenu { Id = 30, ParentId = 0, Type = MenuType.Catalog, Title = "文件管理", Permission = "", Icon = "ph:folder-duotone", Sort = 4, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 文件管理页(R8:SysFileController 列表 + 上传/下载/删除)。上传码 31、分页码 32、下载 79、删除 80。
        new SysMenu { Id = 78, ParentId = 30, Type = MenuType.Menu, Title = "文件管理", Permission = "", Path = "/system/file", Component = "system/file/index", Icon = "ph:files-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 31, ParentId = 30, Type = MenuType.Button, Title = "文件-上传", Permission = "POST:/api/v1/sys/file/upload", Sort = 1, Enabled = true },
        new SysMenu { Id = 32, ParentId = 30, Type = MenuType.Button, Title = "文件-分页", Permission = "GET:/api/v1/sys/file/page", Sort = 2, Enabled = true },
        new SysMenu { Id = 79, ParentId = 30, Type = MenuType.Button, Title = "文件-下载", Permission = "GET:/api/v1/sys/file/{id}/download", Sort = 3, Enabled = true },
        new SysMenu { Id = 80, ParentId = 30, Type = MenuType.Button, Title = "文件-删除", Permission = "DELETE:/api/v1/sys/file/{id}", Sort = 4, Enabled = true },
        new SysMenu { Id = 86, ParentId = 30, Type = MenuType.Button, Title = "文件-批量删除", Permission = "POST:/api/v1/sys/file/batch-delete", Sort = 5, Enabled = true },
        // 分片/断点续传上传(§4 分片上传):init 秒传探测 + chunk 传片 + complete 合并落库。
        new SysMenu { Id = 104, ParentId = 30, Type = MenuType.Button, Title = "文件-分片初始化", Permission = "POST:/api/v1/sys/file/chunk/init", Sort = 6, Enabled = true },
        new SysMenu { Id = 105, ParentId = 30, Type = MenuType.Button, Title = "文件-分片上传", Permission = "POST:/api/v1/sys/file/chunk", Sort = 7, Enabled = true },
        new SysMenu { Id = 106, ParentId = 30, Type = MenuType.Button, Title = "文件-分片完成", Permission = "POST:/api/v1/sys/file/chunk/complete", Sort = 8, Enabled = true },

        // ═══ 业务中心(示例业务模块 Id=2)═══════════════════════════
        // 复用现成的 dashboard/biz.vue;工作台是根级 Menu 节点(可挂 ModuleId),Path 与 system 工作台错开。
        new SysMenu { Id = 109, ParentId = 0, Type = MenuType.Menu, Title = "工作台", Permission = "", Path = "/business/workbench", Component = "dashboard/biz", Icon = "ph:squares-four-duotone", Sort = 0, Enabled = true, Visible = true, ModuleId = DefaultModuleSeed.BUSINESS_MODULE_ID },
    ];
}
