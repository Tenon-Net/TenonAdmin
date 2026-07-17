using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 仅主键基类 <see cref="PrimaryId"/>(#8):明细/子表继承它,不带审计四件套与软删,
/// 但插入时仍由审计 AOP 自动获得雪花 Id —— 该填充按 <c>PrimaryId</c>(而非 <c>BaseEntity</c>)匹配,才覆盖到这类实体。
/// <para>刻意不走 <c>IRepository&lt;T&gt;</c>(它约束 <c>where T : BaseEntity</c>,PrimaryId-only 实体本就用不了),
/// 直接经 <c>ISqlSugarClient</c> 读写,正是主从表里从表的典型用法。</para>
/// </summary>
public class PrimaryIdEntityTests
{
    /// <summary>范例"明细子表"实体:只有主键 + 业务字段,无审计/软删列。</summary>
    [SugarTable("primaryid_detail_test")]
    private sealed class DetailRow : PrimaryId
    {
        [SugarColumn(Length = 32, ColumnDescription = "名称")]
        public string Name { get; set; } = "";
    }

    [Fact]
    public async Task PrimaryIdOnlyEntity_GetsSnowflakeId_OnInsert()
    {
        var id = $"pid-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");

        var services = new ServiceCollection();
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(id, dbFile) });
        await using var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<ISqlSugarClient>();
        db.CodeFirst.InitTables<DetailRow>();   // 无 BaseEntity 也能建表:建表按 [SugarTable] 扫描,不认基类

        var row = new DetailRow { Name = "bom-detail" };   // Id 未指定(=0)
        await db.Insertable(row).ExecuteCommandAsync();

        var stored = await db.Queryable<DetailRow>().FirstAsync(x => x.Name == "bom-detail");
        // ≥ SnowflakeFloor(4096)才证明是 AOP 填的雪花号,而非 SQLite rowid(=1)之类的兜底填充
        Assert.True(stored.Id >= TenonSeedIds.SnowflakeFloor,
            $"PrimaryId-only 实体应由审计 AOP 自动填雪花 Id(≥{TenonSeedIds.SnowflakeFloor}),实际 {stored.Id}");

        TestDb.Cleanup(id, dbFile);
    }
}
