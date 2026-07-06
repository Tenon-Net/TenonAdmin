using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 职位表——组织内岗位/职务的字典项(如"经理""专员"),供用户维度的职位归属使用。
/// <para>与角色(权限载体)是正交概念:职位描述"是什么岗位",角色描述"有什么权限",
/// 二者不互相派生,一名用户可挂一个职位、多个角色。</para>
/// </summary>
[SugarTable("sys_position", TableDescription = "职位")]
[SugarIndex("idx_sys_position_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class SysPosition : BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "职位名称")]
    public string Name { get; set; } = "";

    /// <summary>职位编码(唯一,程序判职位用它而非名称)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "职位编码(唯一)")]
    public string Code { get; set; } = "";

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;
}
