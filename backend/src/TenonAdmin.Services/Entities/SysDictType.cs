using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 字典类型表(设计 §5.7 字典模块)。一个类型下挂若干 <see cref="SysDictItem"/>(通过
/// <see cref="Code"/> 关联,而非外键 Id)——前端按类型编码取下拉数据源。
/// <para><b>Code 创建后不可变</b>:字典项与其它模块都按 Code 关联字典类型,若允许改 Code 会让既有
/// 关联静默失效,故 <c>DictService.UpdateTypeAsync</c> 只改 Name/Sort/Enabled/Remark,不动 Code。</para>
/// </summary>
[SugarTable("sys_dict_type", TableDescription = "字典类型")]
[SugarIndex("idx_sys_dict_type_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class SysDictType : BaseEntity
{
    /// <summary>字典类型编码(唯一,创建后不可变;字典项按它关联)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "字典类型编码(唯一,创建后不可变)")]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 64, ColumnDescription = "字典类型名称")]
    public string Name { get; set; } = "";

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
