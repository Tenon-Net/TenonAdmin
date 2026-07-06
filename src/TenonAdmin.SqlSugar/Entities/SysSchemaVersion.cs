using SqlSugar;

namespace TenonAdmin.SqlSugar;

/// <summary>架构版本表(设计 §16;骨架用它验证 CodeFirst 建表 + 种子)</summary>
[SugarTable("sys_schema_version")]
public class SysSchemaVersion : BaseEntity
{
    [SugarColumn(Length = 32, ColumnDescription = "架构版本")]
    public string Version { get; set; } = "";

    [SugarColumn(ColumnDescription = "应用时间")]
    public DateTime AppliedTime { get; set; }
}
