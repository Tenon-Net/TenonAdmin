using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 字典项表(设计 §5.7 字典模块)。按 <see cref="DictTypeCode"/> 关联所属 <see cref="SysDictType"/>——
/// 用类型编码而非类型 Id,因为类型编码创建后不可变,关联天然稳定。
/// <para>启用中的项列表是前端下拉的高频读数据,由 <c>DictService.GetItemsByTypeAsync</c> 读穿透缓存。</para>
/// </summary>
[SugarTable("sys_dict_item", TableDescription = "字典项")]
[SugarIndex("idx_sys_dict_item_type", nameof(DictTypeCode), OrderByType.Asc, IsUnique = false)]
public class SysDictItem : BaseEntity
{
    /// <summary>所属字典类型编码</summary>
    [SugarColumn(Length = 64, ColumnDescription = "所属字典类型编码")]
    public string DictTypeCode { get; set; } = "";

    [SugarColumn(Length = 64, ColumnDescription = "字典项显示文本")]
    public string Label { get; set; } = "";

    [SugarColumn(Length = 128, ColumnDescription = "字典项值")]
    public string Value { get; set; } = "";

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;
}
