using SqlSugar;

namespace TenonAdmin.SqlSugar;

// ponytail: BaseEntity 暂放 SqlSugar 层(携带 SqlSugar 特性)。
// 设计 §5.6 归 Core;待 Core 改用无特性 POCO + 外部映射时再迁移。
/// <summary>实体基类:主键 + 最小审计字段</summary>
public abstract class BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnDescription = "主键")]
    public long Id { get; set; }

    [SugarColumn(ColumnDescription = "创建时间")]
    public DateTime CreateTime { get; set; }
}
