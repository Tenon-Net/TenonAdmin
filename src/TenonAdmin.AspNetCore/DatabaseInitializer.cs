using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.AspNetCore;

/// <summary>首启初始化:确保目录、CodeFirst 建表、写种子。幂等。</summary>
internal sealed class DatabaseInitializer(
    ISqlSugarClient db,
    AdminDatabaseOptions options,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureSqliteDirectory();

        if (options.EnableCodeFirst)
        {
            db.CodeFirst.InitTables(typeof(SysSchemaVersion));
            logger.LogInformation("TenonAdmin: CodeFirst 建表完成");
        }

        if (options.EnableSeed && !db.Queryable<SysSchemaVersion>().Any())
        {
            db.Insertable(new SysSchemaVersion
            {
                Id = 1,
                Version = "0.0.1",
                CreateTime = DateTime.Now,
                AppliedTime = DateTime.Now,
            }).ExecuteCommand();
            logger.LogInformation("TenonAdmin: 种子写入 schema_version=0.0.1");
        }

        logger.LogWarning("TenonAdmin 已启动(开发骨架)。数据库: {DbType} / {Conn}", options.DbType, options.ConnectionString);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // SQLite 只建文件不建目录,这里补上目录创建
    private void EnsureSqliteDirectory()
    {
        if (!options.DbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) return;
        var marker = "Data Source=";
        var idx = options.ConnectionString.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        var path = options.ConnectionString[(idx + marker.Length)..].Split(';')[0].Trim();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
