using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>系统配置新增/编辑入参(增改共用同一份字段;<c>ConfigKey</c> 编辑时不生效,见 <see cref="ConfigService.UpdateAsync"/>)。</summary>
public record ConfigInput
{
    /// <summary>配置键(唯一,创建后不可修改)</summary>
    public string ConfigKey { get; init; } = "";

    /// <summary>配置值</summary>
    public string? ConfigValue { get; init; }

    /// <summary>配置名称</summary>
    public string Name { get; init; } = "";

    /// <summary>分组编码(可选)</summary>
    public string? GroupCode { get; init; }

    /// <summary>排序(小在前)</summary>
    public int Sort { get; init; }

    /// <summary>备注</summary>
    public string? Remark { get; init; }
}

/// <summary>系统配置分页查询入参:在通用分页基础上加名称/键模糊过滤 + 分组精确过滤。</summary>
public record ConfigPageInput : PageInputBase
{
    /// <summary>配置键(模糊匹配,可选)</summary>
    public string? ConfigKey { get; init; }

    /// <summary>配置名称(模糊匹配,可选)</summary>
    public string? Name { get; init; }

    /// <summary>分组编码(精确匹配,可选)</summary>
    public string? GroupCode { get; init; }
}
