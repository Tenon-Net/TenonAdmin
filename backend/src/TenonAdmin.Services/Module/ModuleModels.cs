namespace TenonAdmin.Services;

/// <summary>模块新增/编辑入参</summary>
public record ModuleInput
{
    /// <summary>模块编码(唯一)</summary>
    public string Code { get; init; } = "";

    /// <summary>模块/应用名称</summary>
    public string Title { get; init; } = "";

    /// <summary>图标</summary>
    public string? Icon { get; init; }

    /// <summary>默认落地路由</summary>
    public string? DefaultRoute { get; init; }

    /// <summary>后端路由匹配前缀(如 sys/biz),用于权限路由下拉按应用软过滤;留空=不过滤</summary>
    public string? ApiPrefix { get; init; }

    /// <summary>排序(小在前)</summary>
    public int Sort { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>备注</summary>
    public string? Remark { get; init; }
}
