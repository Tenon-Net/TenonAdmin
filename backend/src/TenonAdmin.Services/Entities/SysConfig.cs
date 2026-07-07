using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 系统配置表——键值对形式的全局配置项(如站点名称、默认密码策略等),支持按 <see cref="GroupCode"/> 分组管理。
/// <para><see cref="ConfigKey"/> 创建后不可修改:程序按键读取配置值,改键等同于换了一个配置项,
/// 会使既有读取方(硬编码或缓存)全部失联,故只允许改值不允许改键。</para>
/// </summary>
[SugarTable("sys_config", TableDescription = "系统配置")]
[SugarIndex("idx_sys_config_key", nameof(ConfigKey), OrderByType.Asc, IsUnique = true)]
public class SysConfig : BaseEntity
{
    /// <summary>配置键(唯一,程序按它读取配置值;创建后不可修改)</summary>
    [SugarColumn(Length = 128, ColumnDescription = "配置键(唯一,创建后不可修改)")]
    public string ConfigKey { get; set; } = "";

    [SugarColumn(Length = 1024, IsNullable = true, ColumnDescription = "配置值")]
    public string? ConfigValue { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "配置名称")]
    public string Name { get; set; } = "";

    /// <summary>分组编码(用于配置项归类展示,可选)</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "分组编码")]
    public string? GroupCode { get; set; }

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
