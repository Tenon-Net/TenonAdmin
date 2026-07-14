namespace TenonAdmin.SqlSugar;

/// <summary>
/// 架构版本表种子(设计 §12 生产建表安全:内置表用 sys_schema_version 记版本)。
/// 也是 <see cref="ISeedData{TEntity}"/> 机制的第一个消费者——框架种子和用户种子走同一条路。
/// </summary>
internal sealed class SchemaVersionSeed : ISeedData<SysSchemaVersion>
{
    public IEnumerable<SysSchemaVersion> HasData() =>
    [
        // Id 固定为 1:幂等判存靠主键(见 ISeedData 注释);CreateTime 留给 AOP 首插时填充
        new() { Id = 1, Version = SysSchemaVersion.Current, AppliedTime = DateTime.Now },
    ];
}
