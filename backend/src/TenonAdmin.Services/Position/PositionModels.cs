using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>职位新增/编辑入参(增改共用同一份字段)。</summary>
public record PositionInput
{
    /// <summary>职位名称</summary>
    public string Name { get; init; } = "";

    /// <summary>职位编码(唯一)</summary>
    public string Code { get; init; } = "";

    /// <summary>排序(小在前)</summary>
    public int Sort { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>职位分页查询入参:在通用分页基础上加职位名称模糊过滤。</summary>
public record PositionPageInput : PageInputBase
{
    /// <summary>职位名称(模糊匹配,可选)</summary>
    public string? Name { get; init; }
}
