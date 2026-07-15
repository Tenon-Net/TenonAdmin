using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 模块/应用表(多应用门户)——一套登录体系下的多个独立子系统。菜单树按模块分区
/// (<see cref="SysMenu.ModuleId"/> 仅顶级目录设置),登录后一次只进一个应用、只加载该应用的菜单与路由。
/// <para>模块<b>不是独立权限轴</b>:用户"拥有"某模块 = 被授权了该模块下任一菜单(派生自菜单授权,
/// 见 <c>MenuService.GetMyModulesAsync</c>);模块只做运行时的侧边栏/路由分区,不改用户持有的 API 权限码。</para>
/// <para>内置 <c>system</c> 模块(<see cref="DefaultModuleSeed.BUILTIN_MODULE_ID"/>)不可删除。</para>
/// </summary>
[SugarTable("sys_module", TableDescription = "模块/应用")]
[SugarIndex("idx_sys_module_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class SysModule : BaseEntity
{
    /// <summary>模块编码(唯一,程序判模块用它而非名称;内置 system)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "模块编码(唯一)")]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 64, ColumnDescription = "模块/应用名称")]
    public string Title { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "图标")]
    public string? Icon { get; set; }

    /// <summary>切入该应用后的落地路由(前端用;为空则进该应用菜单首项)</summary>
    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "默认落地路由")]
    public string? DefaultRoute { get; set; }

    /// <summary>
    /// 后端路由匹配前缀(如 <c>sys</c>/<c>biz</c>):菜单配权限按钮时,权限路由下拉据此按应用软过滤
    /// (路由 <c>/api/v1/sys/...</c> ↔ 前缀 <c>sys</c>)。仅 UI 降噪,<b>非权限轴</b>;留空=不过滤。
    /// 注意 <see cref="Code"/>(如 system)≠ 路由段(如 sys),故单列显式存前缀,不复用 Code。
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "后端路由匹配前缀(如 sys/biz)")]
    public string? ApiPrefix { get; set; }

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用(停用后不在门户显示)")]
    public bool Enabled { get; set; } = true;

    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
