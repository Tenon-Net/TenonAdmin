namespace TenonAdmin.Services;

/// <summary>机构新增/编辑入参</summary>
public record OrgInput
{
    /// <summary>父机构 Id(0=根,即顶级机构无父)</summary>
    public long ParentId { get; init; }

    /// <summary>机构名称</summary>
    public string Name { get; init; } = "";

    /// <summary>机构编码(唯一)</summary>
    public string Code { get; init; } = "";

    /// <summary>机构分类(org_category 字典 value,可空)</summary>
    public string? Category { get; init; }

    /// <summary>排序(小在前)</summary>
    public int Sort { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;
}
