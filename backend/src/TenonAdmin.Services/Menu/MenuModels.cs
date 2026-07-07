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
