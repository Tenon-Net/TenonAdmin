using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 角色表(设计 §16)。角色是"权限码集合"的载体:用户挂角色、角色挂菜单(路由权限码),
/// 用户的权限 = 其全部角色所授菜单权限码的并集(聚合见 <c>RbacPermissionProvider</c>)。
/// <para>数据范围(五种机构范围)在 T3 由独立表 <c>sys_role_data_scope</c> 承载,不揉进本表。</para>
/// </summary>
[SugarTable("sys_role", TableDescription = "角色")]
[SugarIndex("idx_sys_role_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class SysRole : BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "角色名称")]
    public string Name { get; set; } = "";

    /// <summary>角色编码(唯一,程序判角色用它而非名称)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "角色编码(唯一)")]
    public string Code { get; set; } = "";

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
