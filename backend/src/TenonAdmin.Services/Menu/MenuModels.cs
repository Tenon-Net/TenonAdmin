namespace TenonAdmin.Services;

/// <summary>门户模块出参(当前用户视角的可访问应用)。</summary>
public record ModuleItem
{
    public required long Id { get; init; }
    public required string Code { get; init; }
    public required string Title { get; init; }
    public string? Icon { get; init; }
    public string? DefaultRoute { get; init; }
    public int Sort { get; init; }
}

/// <summary>"我的模块"聚合出参:可访问模块列表 + 用户的默认模块 Id。</summary>
public record MyModulesOutput
{
    public required IReadOnlyList<ModuleItem> Modules { get; init; }

    /// <summary>用户设置的默认应用 Id;可空(未设=前端让用户选)。</summary>
    public long? DefaultModuleId { get; init; }
}

/// <summary>侧边栏菜单树节点(前端动态路由用)。</summary>
public record MenuNode
{
    public required long Id { get; init; }
    public long ParentId { get; init; }
    public MenuType Type { get; init; }
    public required string Title { get; init; }
    public string? Path { get; init; }
    public string? Component { get; init; }
    public string? Icon { get; init; }
    public int Sort { get; init; }
    public bool Visible { get; init; }

    /// <summary>子节点(按 Sort、Id 排序)。</summary>
    public List<MenuNode> Children { get; init; } = [];
}

/// <summary>
/// 菜单管理端树节点(<b>全字段</b>:含权限码 / 启用 / 所属模块 / 按钮节点)。
/// 与门户 <see cref="MenuNode"/> 区分——后者只承载前端导航需要的字段、且不含按钮。
/// </summary>
public record MenuTreeNode
{
    public required long Id { get; init; }
    public long ParentId { get; init; }
    public MenuType Type { get; init; }
    public required string Title { get; init; }
    public string Permission { get; init; } = "";
    public int Sort { get; init; }
    public bool Enabled { get; init; }
    public long? ModuleId { get; init; }
    public string? Path { get; init; }
    public string? Component { get; init; }
    public string? Icon { get; init; }
    public bool Visible { get; init; }

    /// <summary>子节点(按 Sort、Id 排序)。</summary>
    public List<MenuTreeNode> Children { get; init; } = [];
}

/// <summary>
/// 菜单新增/编辑入参。<see cref="ModuleId"/> <b>仅顶级目录(ParentId==0)有效</b>,
/// 服务端对子节点强制置空——模块归属由树上溯根目录解析(见 <c>MenuService</c>),子节点不冗余存。
/// </summary>
public record MenuInput
{
    public long ParentId { get; init; }
    public MenuType Type { get; init; } = MenuType.Menu;
    public string Title { get; init; } = "";

    /// <summary>规范化路由权限码(页面/按钮节点用),形如 <c>GET:/api/v1/ping</c>;目录留空。</summary>
    public string Permission { get; init; } = "";
    public int Sort { get; init; }
    public bool Enabled { get; init; } = true;
    public long? ModuleId { get; init; }
    public string? Path { get; init; }
    public string? Component { get; init; }
    public string? Icon { get; init; }
    public bool Visible { get; init; } = true;
}
