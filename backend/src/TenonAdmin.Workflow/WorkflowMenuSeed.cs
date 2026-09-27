using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流卫星包菜单种子。
/// <para>员工侧(发起/待办/抄送/我发起的/我已办的)挂在示例 <c>business</c>「业务中心」;
/// 管理侧(定义/设计器/监控/长期委托)挂在内置 <c>system</c>「系统」。
/// 模块归属只写在顶级目录的 <see cref="SysMenu.ModuleId"/> 上,子节点靠上溯解析。</para>
/// </summary>
internal sealed class WorkflowMenuSeed : ISeedData<SysMenu>
{
    private const long AdminRootId = TenonSeedIds.ConsumerMin + 47_000;
    private const long DefinitionId = AdminRootId + 1;
    private const long StartId = AdminRootId + 20;
    private const long TodoId = AdminRootId + 21;
    private const long CcId = AdminRootId + 22;
    // 设计器是列表页的二级页,不进侧边栏(Visible=false);但路由只从菜单树生成,
    // 所以它必须有自己的菜单行,否则 `/workflow/definition/designer` 无路由可跳。
    private const long DesignerId = AdminRootId + 23;
    private const long MineId = AdminRootId + 24;
    private const long DoneId = AdminRootId + 25;
    private const long MonitorId = AdminRootId + 26;
    private const long DelegationId = AdminRootId + 30;
    /// <summary>业务中心下的「审批中心」目录(与 AdminRootId 拆开,才能分属不同 ModuleId)。</summary>
    private const long EmployeeRootId = AdminRootId + 50;
    // 与内核 DefaultModuleSeed 同值;那些常量是 internal,跨程序集取不到,故此处复制。
    private const long BuiltInModuleId = 1;
    private const long BusinessModuleId = 2;

    public bool SyncOnUpgrade => true;

    public IEnumerable<SysMenu> HasData() =>
    [
        // ── 系统应用:流程治理 ──────────────────────────────────────────
        new() { Id = AdminRootId, ParentId = 0, Type = MenuType.Catalog, Title = "流程管理", Permission = "", Icon = "ph:tree-structure-duotone", Sort = 6, Enabled = true, Visible = true, ModuleId = BuiltInModuleId },
        new() { Id = DefinitionId, ParentId = AdminRootId, Type = MenuType.Menu, Title = "流程定义", Permission = "", Path = "/workflow/definition", Component = "workflow/definition/index", Icon = "ph:tree-structure-duotone", Sort = 1, Enabled = true, Visible = true },
        new() { Id = AdminRootId + 2, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-分页", Permission = "GET:/api/v1/workflow/definition/page", Sort = 1, Enabled = true },
        new() { Id = AdminRootId + 3, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-详情", Permission = "GET:/api/v1/workflow/definition/{id}", Sort = 2, Enabled = true },
        new() { Id = AdminRootId + 4, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-版本", Permission = "GET:/api/v1/workflow/definition/versions/{id}", Sort = 3, Enabled = true },
        new() { Id = AdminRootId + 5, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-新增", Permission = "POST:/api/v1/workflow/definition/add", Sort = 4, Enabled = true },
        new() { Id = AdminRootId + 6, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-更新", Permission = "POST:/api/v1/workflow/definition/update", Sort = 5, Enabled = true },
        new() { Id = AdminRootId + 7, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-发布", Permission = "POST:/api/v1/workflow/definition/publish", Sort = 6, Enabled = true },
        new() { Id = AdminRootId + 8, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-停用", Permission = "POST:/api/v1/workflow/definition/disable", Sort = 7, Enabled = true },
        new() { Id = AdminRootId + 9, ParentId = DefinitionId, Type = MenuType.Button, Title = "流程定义-删除", Permission = "DELETE:/api/v1/workflow/definition/{id}", Sort = 8, Enabled = true },
        new() { Id = MonitorId, ParentId = AdminRootId, Type = MenuType.Menu, Title = "流程监控", Permission = "", Path = "/workflow/monitor", Component = "workflow/monitor/index", Icon = "ph:monitor-duotone", Sort = 2, Enabled = true, Visible = true },
        new() { Id = AdminRootId + 13, ParentId = MonitorId, Type = MenuType.Button, Title = "流程监控-分页", Permission = "GET:/api/v1/workflow/instance/monitor", Sort = 1, Enabled = true },
        new() { Id = AdminRootId + 41, ParentId = MonitorId, Type = MenuType.Button, Title = "流程监控-outbox 分页", Permission = "GET:/api/v1/workflow/outbox/page", Sort = 2, Enabled = true },
        new() { Id = AdminRootId + 42, ParentId = MonitorId, Type = MenuType.Button, Title = "流程监控-outbox 重放", Permission = "POST:/api/v1/workflow/outbox/{id}/replay", Sort = 3, Enabled = true },
        new() { Id = DelegationId, ParentId = AdminRootId, Type = MenuType.Menu, Title = "长期委托", Permission = "", Path = "/workflow/delegation", Component = "workflow/delegation/index", Icon = "ph:arrows-left-right-duotone", Sort = 3, Enabled = true, Visible = true },
        new() { Id = DelegationId + 1, ParentId = DelegationId, Type = MenuType.Button, Title = "长期委托-分页", Permission = "GET:/api/v1/workflow/delegation/page", Sort = 1, Enabled = true },
        new() { Id = DelegationId + 2, ParentId = DelegationId, Type = MenuType.Button, Title = "长期委托-新增", Permission = "POST:/api/v1/workflow/delegation/add", Sort = 2, Enabled = true },
        new() { Id = DelegationId + 3, ParentId = DelegationId, Type = MenuType.Button, Title = "长期委托-更新", Permission = "PUT:/api/v1/workflow/delegation/{id}", Sort = 3, Enabled = true },
        new() { Id = DelegationId + 4, ParentId = DelegationId, Type = MenuType.Button, Title = "长期委托-删除", Permission = "DELETE:/api/v1/workflow/delegation/{id}", Sort = 4, Enabled = true },
        new() { Id = DesignerId, ParentId = AdminRootId, Type = MenuType.Menu, Title = "流程设计器", Permission = "", Path = "/workflow/definition/designer", Component = "workflow/definition/designer", Sort = 98, Enabled = true, Visible = false },

        // ── 业务中心:员工日常审批 ────────────────────────────────────
        new() { Id = EmployeeRootId, ParentId = 0, Type = MenuType.Catalog, Title = "审批中心", Permission = "", Icon = "ph:flow-arrow-duotone", Sort = 1, Enabled = true, Visible = true, ModuleId = BusinessModuleId },
        new() { Id = StartId, ParentId = EmployeeRootId, Type = MenuType.Menu, Title = "发起流程", Permission = "", Path = "/workflow/start", Component = "workflow/start/index", Icon = "ph:paper-plane-tilt-duotone", Sort = 1, Enabled = true, Visible = true },
        new() { Id = TodoId, ParentId = EmployeeRootId, Type = MenuType.Menu, Title = "待我审批", Permission = "", Path = "/workflow/todo", Component = "workflow/todo/index", Icon = "ph:check-square-offset-duotone", Sort = 2, Enabled = true, Visible = true },
        new() { Id = AdminRootId + 27, ParentId = TodoId, Type = MenuType.Button, Title = "审批-加签", Permission = "POST:/api/v1/workflow/task/add-sign", Sort = 4, Enabled = true },
        new() { Id = AdminRootId + 28, ParentId = TodoId, Type = MenuType.Button, Title = "审批-减签", Permission = "POST:/api/v1/workflow/task/remove-sign", Sort = 5, Enabled = true },
        new() { Id = AdminRootId + 29, ParentId = TodoId, Type = MenuType.Button, Title = "审批-拿回", Permission = "POST:/api/v1/workflow/task/take-back", Sort = 6, Enabled = true },
        new() { Id = CcId, ParentId = EmployeeRootId, Type = MenuType.Menu, Title = "抄送我的", Permission = "", Path = "/workflow/cc", Component = "workflow/cc/index", Icon = "ph:copy-duotone", Sort = 3, Enabled = true, Visible = true },
        new() { Id = AdminRootId + 10, ParentId = CcId, Type = MenuType.Button, Title = "抄送-分页", Permission = "GET:/api/v1/workflow/cc/page", Sort = 1, Enabled = true },
        new() { Id = MineId, ParentId = EmployeeRootId, Type = MenuType.Menu, Title = "我发起的", Permission = "", Path = "/workflow/mine", Component = "workflow/mine/index", Icon = "ph:user-circle-duotone", Sort = 4, Enabled = true, Visible = true },
        new() { Id = AdminRootId + 11, ParentId = MineId, Type = MenuType.Button, Title = "我发起的-分页", Permission = "GET:/api/v1/workflow/instance/page", Sort = 1, Enabled = true },
        new() { Id = DoneId, ParentId = EmployeeRootId, Type = MenuType.Menu, Title = "我已办的", Permission = "", Path = "/workflow/done", Component = "workflow/done/index", Icon = "ph:archive-duotone", Sort = 5, Enabled = true, Visible = true },
        new() { Id = AdminRootId + 12, ParentId = DoneId, Type = MenuType.Button, Title = "我已办的-分页", Permission = "GET:/api/v1/workflow/task/done", Sort = 1, Enabled = true },
    ];
}
