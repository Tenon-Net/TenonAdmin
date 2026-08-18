using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>工作流卫星包的最小 M1 菜单与定义管理权限。</summary>
internal sealed class WorkflowMenuSeed : ISeedData<SysMenu>
{
    private const long RootId = TenonSeedIds.ConsumerMin + 47_000;
    private const long DefinitionId = RootId + 1;
    private const long StartId = RootId + 20;
    private const long TodoId = RootId + 21;
    // 设计器是列表页的二级页,不进侧边栏(Visible=false);但路由只从菜单树生成,
    // 所以它必须有自己的菜单行,否则 `/workflow/definition/designer` 无路由可跳。
    private const long DesignerId = RootId + 23;
    // 与内核 DefaultModuleSeed.BUILTIN_MODULE_ID 同值;那个常量是 internal,跨程序集取不到,故此处复制一份。
    private const long BuiltInModuleId = 1;

    public bool SyncOnUpgrade => true;

    public IEnumerable<SysMenu> HasData() =>
    [
        new() { Id = RootId, ParentId = 0, Type = MenuType.Catalog, Title = "审批中心", Permission = "", Icon = "ph:flow-arrow-duotone", Sort = 6, Enabled = true, Visible = true, ModuleId = BuiltInModuleId },
        new() { Id = DefinitionId, ParentId = RootId, Type = MenuType.Menu, Title = "流程定义", Permission = "", Path = "/workflow/definition", Component = "workflow/definition/index", Icon = "ph:tree-structure-duotone", Sort = 1, Enabled = true, Visible = true },
        new() { Id = RootId + 2, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-分页", Permission = "GET:/api/v1/workflow/definition/page", Sort = 1, Enabled = true },
        new() { Id = RootId + 3, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-详情", Permission = "GET:/api/v1/workflow/definition/{id}", Sort = 2, Enabled = true },
        new() { Id = RootId + 4, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-版本", Permission = "GET:/api/v1/workflow/definition/versions/{id}", Sort = 3, Enabled = true },
        new() { Id = RootId + 5, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-新增", Permission = "POST:/api/v1/workflow/definition/add", Sort = 4, Enabled = true },
        new() { Id = RootId + 6, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-更新", Permission = "POST:/api/v1/workflow/definition/update", Sort = 5, Enabled = true },
        new() { Id = RootId + 7, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-发布", Permission = "POST:/api/v1/workflow/definition/publish", Sort = 6, Enabled = true },
        new() { Id = RootId + 8, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-停用", Permission = "POST:/api/v1/workflow/definition/disable", Sort = 7, Enabled = true },
        new() { Id = RootId + 9, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-删除", Permission = "DELETE:/api/v1/workflow/definition/{id}", Sort = 8, Enabled = true },
        new() { Id = StartId, ParentId = RootId, Type = MenuType.Menu, Title = "发起流程", Permission = "", Path = "/workflow/start", Component = "workflow/start/index", Icon = "ph:paper-plane-tilt-duotone", Sort = 2, Enabled = true, Visible = true },
        new() { Id = TodoId, ParentId = RootId, Type = MenuType.Menu, Title = "待我审批", Permission = "", Path = "/workflow/todo", Component = "workflow/todo/index", Icon = "ph:check-square-offset-duotone", Sort = 3, Enabled = true, Visible = true },
        new() { Id = DesignerId, ParentId = RootId, Type = MenuType.Menu, Title = "流程设计器", Permission = "", Path = "/workflow/definition/designer", Component = "workflow/definition/designer", Sort = 98, Enabled = true, Visible = false },
    ];
}
