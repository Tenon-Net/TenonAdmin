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

/// <summary>批量存值入参项:分类配置中心的结构化表单按键回写<b>值</b>(不碰 Name/GroupCode/Sort)。</summary>
public record ConfigBatchItem
{
    /// <summary>配置键</summary>
    public string ConfigKey { get; init; } = "";

    /// <summary>配置值</summary>
    public string? ConfigValue { get; init; }
}

/// <summary>站点信息(匿名可读的展示类配置白名单;登录前/无配置读权限的用户也能取)。</summary>
public record SiteInfoOutput
{
    /// <summary>站点标题(浏览器标题/登录页展示名)</summary>
    public string? Title { get; init; }

    /// <summary>是否启用登录验证码(运行时配置驱动;前端据此决定登录页是否展示验证码)。</summary>
    public bool CaptchaEnabled { get; init; }
}
