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

    /// <summary>
    /// 是否可被非超管转授给他人(QA36 角色委派)。可空以兼容存量库的无损升级:
    /// 数据库 NULL(功能上线前的存量角色)与显式 <c>false</c> 同判定为"不可转授",只有显式 <c>true</c> 才放行——
    /// 安全默认(未标注的旧角色一律收紧,不因升级静默放宽)。演进列必须可空:MSSQL 无法对有数据的表
    /// ADD 无 DEFAULT 的 NOT NULL 列(同 <see cref="SysUser.ForceTotp"/> 的成法)。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "是否可转授(非超管可授予)")]
    public bool? IsDelegatable { get; set; }
}
