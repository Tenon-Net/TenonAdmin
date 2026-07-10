using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>字典类型新增/编辑入参。<c>Code</c> 仅新增时生效——更新时服务层忽略它,Code 创建后不可变。</summary>
public record DictTypeInput
{
    /// <summary>字典类型编码(唯一,创建后不可变)</summary>
    public string Code { get; init; } = "";

    /// <summary>字典类型名称</summary>
    public string Name { get; init; } = "";

    /// <summary>排序(小在前)</summary>
    public int Sort { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>备注</summary>
    public string? Remark { get; init; }
}

/// <summary>字典类型分页查询入参:在通用分页基础上加编码/名称模糊过滤。</summary>
public record DictTypePageInput : PageInputBase
{
    /// <summary>字典类型编码(模糊匹配,可选)</summary>
    public string? Code { get; init; }

    /// <summary>字典类型名称(模糊匹配,可选)</summary>
    public string? Name { get; init; }
}

/// <summary>字典项分页查询入参(管理端:按类型取<b>含停用</b>的全部项)。</summary>
public record DictItemPageInput : PageInputBase
{
    /// <summary>所属字典类型编码(精确匹配)</summary>
    public string TypeCode { get; init; } = "";
}

/// <summary>字典项新增/编辑入参</summary>
public record DictItemInput
{
    /// <summary>所属字典类型编码</summary>
    public string DictTypeCode { get; init; } = "";

    /// <summary>字典项显示文本</summary>
    public string Label { get; init; } = "";

    /// <summary>字典项值</summary>
    public string Value { get; init; } = "";

    /// <summary>排序(小在前)</summary>
    public int Sort { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;
}
