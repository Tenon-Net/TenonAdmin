using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 内置模块种子(多应用门户,§4)。播两个应用:内置 <c>system</c>(默认工作区,现有系统管理/组织/字典/文件等
/// 顶级目录都挂靠它,见 <c>DefaultMenuSeed</c>,受 <c>ModuleService.DeleteAsync</c> 保护不可删)+ 一个示例
/// <c>business</c>「业务中心」(仅一条工作台菜单,演示多应用门户,可删)。固定 Id 作幂等锚点。
/// </summary>
internal sealed class DefaultModuleSeed : ISeedData<SysModule>
{
    /// <summary>内置模块固定主键(种子幂等锚点 + 删除保护锚点)</summary>
    internal const long BUILTIN_MODULE_ID = 1;

    /// <summary>内置模块编码</summary>
    internal const string BUILTIN_MODULE_CODE = "system";

    /// <summary>示例业务模块固定主键(挂工作台演示菜单,可删)</summary>
    internal const long BUSINESS_MODULE_ID = 2;

    /// <summary>模块表是内核拥有的结构,随内核升级同步(见 <see cref="ISeedData{T}.SyncOnUpgrade"/>)。</summary>
    public bool SyncOnUpgrade => true;

    public IEnumerable<SysModule> HasData() =>
    [
        new SysModule { Id = BUILTIN_MODULE_ID, Code = BUILTIN_MODULE_CODE, Title = "系统", Icon = "lucide:settings", DefaultRoute = "", Sort = 1, Enabled = true, Remark = "内置系统应用,不可删除" },
        new SysModule { Id = BUSINESS_MODULE_ID, Code = "business", Title = "业务中心", Icon = "lucide:briefcase-business", DefaultRoute = "", Sort = 2, Enabled = true, Remark = "示例业务应用(可删除)" },
    ];
}
