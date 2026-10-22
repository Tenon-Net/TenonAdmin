using MySqlConnector;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// issue #85：任务日志的两个热查询必须由实体索引覆盖；存量 MySQL 可用升级脚本在线补齐。
/// 这里查真实数据库目录，不以实体特性反射代替建表结果。
/// </summary>
public sealed class JobLogIndexTests : IDisposable
{
    private const string JobEndIndex = "idx_sys_job_log_job_end";
    private const string EndStartIndex = "idx_sys_job_log_end_start";
    private readonly string _identity = $"job-log-index-{Guid.NewGuid():N}";
    private readonly string _dbFile;

    public JobLogIndexTests() =>
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_identity}.db");

    public void Dispose() => TestDb.Cleanup(_identity, _dbFile);

    [Fact]
    public void New_database_has_all_job_log_indexes_with_the_expected_column_order()
    {
        using var db = OpenClient();

        db.CodeFirst.InitTables(typeof(SysJobLog));

        AssertIndex(db, "idx_sys_job_log", "JobId", "CreateTime");
        AssertIndex(db, JobEndIndex, "JobId", "EndTime");
        AssertIndex(db, EndStartIndex, "EndTime", "StartTime");
    }

    [Fact]
    public void InitTables_restores_missing_indexes_on_populated_table_and_is_idempotent()
    {
        using var db = OpenClient();
        db.CodeFirst.InitTables(typeof(SysJobLog));
        db.Insertable(NewLog(1, 17, DateTime.UtcNow, DateTime.UtcNow)).ExecuteCommand();
        DropIndex(db, JobEndIndex);
        DropIndex(db, EndStartIndex);
        Assert.Empty(ReadIndexColumns(db, JobEndIndex));
        Assert.Empty(ReadIndexColumns(db, EndStartIndex));

        db.CodeFirst.InitTables(typeof(SysJobLog));
        db.CodeFirst.InitTables(typeof(SysJobLog));

        Assert.Equal(1, db.Queryable<SysJobLog>().Count());
        AssertIndex(db, JobEndIndex, "JobId", "EndTime");
        AssertIndex(db, EndStartIndex, "EndTime", "StartTime");
    }

    [Fact]
    public void MySql_upgrade_script_restores_old_database_and_can_run_twice()
    {
        if (!TestDb.UseMySql) return;

        using var db = OpenClient();
        db.CodeFirst.InitTables(typeof(SysJobLog));
        db.Insertable(NewLog(1, 23, DateTime.UtcNow, DateTime.UtcNow)).ExecuteCommand();
        DropIndex(db, JobEndIndex);
        DropIndex(db, EndStartIndex);

        ExecuteMySqlUpgradeScript(db.CurrentConnectionConfig.ConnectionString);
        ExecuteMySqlUpgradeScript(db.CurrentConnectionConfig.ConnectionString);

        Assert.Equal(1, db.Queryable<SysJobLog>().Count());
        AssertIndex(db, JobEndIndex, "JobId", "EndTime");
        AssertIndex(db, EndStartIndex, "EndTime", "StartTime");
    }

    [Fact]
    public void MySql_upgrade_script_accepts_equivalent_indexes_with_different_names()
    {
        if (!TestDb.UseMySql) return;

        using var db = OpenClient();
        db.CodeFirst.InitTables(typeof(SysJobLog));
        DropIndex(db, JobEndIndex);
        DropIndex(db, EndStartIndex);
        db.Ado.ExecuteCommand(
            "ALTER TABLE `sys_job_log` " +
            "ADD INDEX `dba_job_end` (`JobId`, `EndTime`), " +
            "ADD INDEX `dba_end_start` (`EndTime`, `StartTime`)");

        ExecuteMySqlUpgradeScript(db.CurrentConnectionConfig.ConnectionString);

        Assert.Empty(ReadIndexColumns(db, JobEndIndex));
        Assert.Empty(ReadIndexColumns(db, EndStartIndex));
        AssertIndex(db, "dba_job_end", "JobId", "EndTime");
        AssertIndex(db, "dba_end_start", "EndTime", "StartTime");
    }

    [Fact]
    public void MySql_hot_queries_choose_the_matching_indexes()
    {
        if (!TestDb.UseMySql) return;

        using var db = OpenClient();
        db.CodeFirst.InitTables(typeof(SysJobLog));
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        const int jobCount = 100;
        const int completedCount = 6_000;
        const int runningCount = 1_000;

        // 每个任务有多条历史和运行记录，使 (JobId, EndTime) 比单列前缀更有选择性。
        // 运行记录里仅少量已经超时，使 (EndTime, StartTime) 的第二列也参与缩小扫描范围。
        var rows = new List<SysJobLog>(500);
        for (var i = 0; i < completedCount + runningCount; i++)
        {
            var running = i >= completedCount;
            var runningOrdinal = i - completedCount;
            var start = running && runningOrdinal < 5 ? now.AddHours(-2) : now.AddMinutes(-5);
            rows.Add(NewLog(i + 1, i % jobCount, start, running ? null : start.AddSeconds(1)));
            if (rows.Count != 500) continue;
            db.Insertable(rows).ExecuteCommand();
            rows.Clear();
        }
        if (rows.Count > 0) db.Insertable(rows).ExecuteCommand();
        db.Ado.ExecuteCommand("ANALYZE TABLE `sys_job_log`");

        var serialCheck = Explain(db,
            "SELECT 1 FROM `sys_job_log` WHERE `JobId` = 42 AND `EndTime` IS NULL LIMIT 1");
        Assert.Equal(JobEndIndex, serialCheck.Key);
        Assert.NotEqual("ALL", serialCheck.AccessType);

        var orphanScan = Explain(db,
            "SELECT `Id`, `NodeName`, `NodeInstanceId` FROM `sys_job_log` " +
            "WHERE `EndTime` IS NULL AND `StartTime` < '2026-01-01 11:00:00'");
        Assert.Equal(EndStartIndex, orphanScan.Key);
        Assert.NotEqual("ALL", orphanScan.AccessType);
    }

    private SqlSugarClient OpenClient()
    {
        Assert.True(Enum.TryParse<DbType>(TestDb.DbType, true, out var dbType));
        return new SqlSugarClient(new ConnectionConfig
        {
            ConfigId = $"JobLogIndex-{Guid.NewGuid():N}",
            DbType = dbType,
            ConnectionString = TestDb.ConnectionString(_identity, _dbFile),
            IsAutoCloseConnection = true,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        });
    }

    private static SysJobLog NewLog(long id, long jobId, DateTime start, DateTime? end) => new()
    {
        Id = id,
        JobId = jobId,
        JobName = $"job-{jobId}",
        FireInstanceId = id,
        ScheduledTime = start,
        StartTime = start,
        EndTime = end,
        RunStatus = end is null ? JobRunStatus.Running : JobRunStatus.Success,
        NodeName = "index-test",
        NodeInstanceId = "index-test",
        CreateTime = start,
    };

    private static void AssertIndex(ISqlSugarClient db, string indexName, params string[] expectedColumns)
    {
        var actual = ReadIndexColumns(db, indexName);
        Assert.Equal(expectedColumns.Length, actual.Count);
        for (var i = 0; i < expectedColumns.Length; i++)
            Assert.True(string.Equals(expectedColumns[i], actual[i], StringComparison.OrdinalIgnoreCase),
                $"索引 {indexName} 第 {i + 1} 列应为 {expectedColumns[i]}，实际为 {actual[i]}。");
    }

    private static List<string> ReadIndexColumns(ISqlSugarClient db, string indexName)
    {
        var sql = db.CurrentConnectionConfig.DbType switch
        {
            DbType.MySql => $"""
                SELECT COLUMN_NAME
                FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'sys_job_log' AND INDEX_NAME = '{indexName}'
                ORDER BY SEQ_IN_INDEX
                """,
            DbType.PostgreSQL => $"""
                SELECT attribute.attname AS COLUMN_NAME
                FROM pg_class AS table_class
                JOIN pg_namespace AS schema ON schema.oid = table_class.relnamespace
                JOIN pg_index AS index_def ON index_def.indrelid = table_class.oid
                JOIN pg_class AS index_class ON index_class.oid = index_def.indexrelid
                CROSS JOIN LATERAL unnest(index_def.indkey) WITH ORDINALITY AS key_column(attnum, position)
                JOIN pg_attribute AS attribute
                  ON attribute.attrelid = table_class.oid AND attribute.attnum = key_column.attnum
                WHERE schema.nspname = current_schema()
                  AND table_class.relname = 'sys_job_log' AND index_class.relname = '{indexName}'
                ORDER BY key_column.position
                """,
            DbType.SqlServer => $"""
                SELECT column_def.name AS COLUMN_NAME
                FROM sys.indexes AS index_def
                JOIN sys.tables AS table_def ON table_def.object_id = index_def.object_id
                JOIN sys.index_columns AS index_column
                  ON index_column.object_id = index_def.object_id AND index_column.index_id = index_def.index_id
                JOIN sys.columns AS column_def
                  ON column_def.object_id = index_column.object_id AND column_def.column_id = index_column.column_id
                WHERE table_def.name = 'sys_job_log' AND index_def.name = '{indexName}'
                  AND index_column.key_ordinal > 0
                ORDER BY index_column.key_ordinal
                """,
            _ => $"""
                SELECT name AS COLUMN_NAME
                FROM pragma_index_info('{indexName}')
                ORDER BY seqno
                """,
        };

        var table = db.Ado.GetDataTable(sql);
        return table.Rows.Cast<System.Data.DataRow>().Select(row => Convert.ToString(row[0])!).ToList();
    }

    private static void DropIndex(ISqlSugarClient db, string indexName)
    {
        var sql = db.CurrentConnectionConfig.DbType switch
        {
            DbType.MySql => $"ALTER TABLE `sys_job_log` DROP INDEX `{indexName}`",
            DbType.SqlServer => $"DROP INDEX [{indexName}] ON [sys_job_log]",
            DbType.PostgreSQL => $"DROP INDEX \"{indexName}\"",
            _ => $"DROP INDEX \"{indexName}\"",
        };
        db.Ado.ExecuteCommand(sql);
    }

    private static (string? Key, string? AccessType, long EstimatedRows) Explain(ISqlSugarClient db, string query)
    {
        var plan = db.Ado.GetDataTable("EXPLAIN " + query);
        var row = plan.Rows[0];
        return (
            Convert.ToString(row["key"]),
            Convert.ToString(row["type"]),
            Convert.ToInt64(row["rows"]));
    }

    private static void ExecuteMySqlUpgradeScript(string connectionString)
    {
        var scriptPath = FindRepositoryFile("scripts", "sql", "mysql", "job-log-indexes.sql");
        var builder = new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true };
        using var connection = new MySqlConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(scriptPath);
        command.ExecuteNonQuery();
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"无法从 {AppContext.BaseDirectory} 向上找到 {Path.Combine(parts)}。");
    }
}
