using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 系统配置种子(T5)。播一条站点标题作为起步样例与冒烟锚点;固定 Id 保幂等,
/// 界面上改配置值不会被重启覆盖(种子只补缺失行)。
/// </summary>
internal sealed class ConfigSeed : ISeedData<SysConfig>
{
    /// <summary>站点标题配置键(前端标题/登录页文案取值)</summary>
    internal const string SITE_TITLE_KEY = "sys.site.title";

    public IEnumerable<SysConfig> HasData() =>
    [
        new SysConfig { Id = 1, ConfigKey = SITE_TITLE_KEY, ConfigValue = "TenonAdmin", Name = "站点标题", GroupCode = "sys", Sort = 1, Remark = "浏览器标题与登录页展示名" },
    ];
}
