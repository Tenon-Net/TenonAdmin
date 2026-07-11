using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>角色新增/编辑入参(增改共用同一份字段)。</summary>
public record RoleInput
{
    /// <summary>角色名称</summary>
    public string Name { get; init; } = "";

    /// <summary>角色编码(唯一)</summary>
    public string Code { get; init; } = "";

    /// <summary>排序(小在前)</summary>
    public int Sort { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>备注</summary>
    public string? Remark { get; init; }
}

/// <summary>角色分页查询入参:在通用分页基础上加角色名称模糊过滤。</summary>
public record RolePageInput : PageInputBase
{
    /// <summary>角色名称(模糊匹配,可选)</summary>
    public string? Name { get; init; }
}
