using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 内置模块种子(多应用门户,§4)。播一个内置 <c>system</c> 应用作为默认工作区——
/// 现有系统管理/组织/字典/文件等顶级目录都挂靠它(见 <c>DefaultMenuSeed</c>)。
/// 固定 Id(幂等锚点),且受 <c>ModuleService.DeleteAsync</c> 保护不可删除。
/// </summary>
internal sealed class DefaultModuleSeed : ISeedData<SysModule>
{
    /// <summary>内置模块固定主键(种子幂等锚点 + 删除保护锚点)</summary>
    internal const long BUILTIN_MODULE_ID = 1;

    /// <summary>内置模块编码</summary>
    internal const string BUILTIN_MODULE_CODE = "system";

    public IEnumerable<SysModule> HasData() =>
    [
        new SysModule { Id = BUILTIN_MODULE_ID, Code = BUILTIN_MODULE_CODE, Title = "系统", Sort = 1, Enabled = true, Remark = "内置系统应用,不可删除" },
    ];
}
