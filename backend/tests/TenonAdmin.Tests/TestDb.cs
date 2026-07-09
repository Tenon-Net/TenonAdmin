using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace TenonAdmin.Tests;

/// <summary>
/// 集成测试的数据库选择(§8 数据库矩阵)。默认 SQLite(本地 <c>dotnet test</c> 不变);CI/矩阵的其它腿
/// 通过环境变量切换:
/// <list type="bullet">
/// <item><c>TENON_TEST_DBTYPE=MySql</c> + <c>TENON_TEST_MYSQL=</c>(不含 Database 的服务器连接串)。</item>
/// <item><c>TENON_TEST_DBTYPE=SqlServer</c> + <c>TENON_TEST_SQLSERVER=</c>(不含 Database 的服务器连接串,
/// 需带 <c>TrustServerCertificate=True;Encrypt=False</c> —— MDS 4.x 默认加密且本地无受信证书)。</item>
/// </list>
/// <para>库隔离:库名由 <c>identity</c> 确定性派生——同 identity → 同库(支持"同库二次启动"的幂等用例),
/// 不同 identity → 各自独立库。建库/删库经原始 <see cref="MySqlConnection"/> / <see cref="SqlConnection"/>
/// 连服务器(SqlSugar 不负责建库,CodeFirst 只建表)。</para>
/// </summary>
internal static class TestDb
{
    public static bool UseMySql =>
        string.Equals(Environment.GetEnvironmentVariable("TENON_TEST_DBTYPE"), "MySql", StringComparison.OrdinalIgnoreCase);

    public static bool UseSqlServer =>
        string.Equals(Environment.GetEnvironmentVariable("TENON_TEST_DBTYPE"), "SqlServer", StringComparison.OrdinalIgnoreCase);

    private static string MySqlBase =>
        Environment.GetEnvironmentVariable("TENON_TEST_MYSQL")
        ?? "Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;";

    private static string SqlServerBase =>
        Environment.GetEnvironmentVariable("TENON_TEST_SQLSERVER")
        ?? "Server=127.0.0.1;User ID=sa;Password=sa;TrustServerCertificate=True;Encrypt=False;";

    /// <summary>当前腿的 DbType 字符串(直接喂给 <c>AdminDatabaseOptions.DbType</c> / 配置)。</summary>
    public static string DbType => UseMySql ? "MySql" : UseSqlServer ? "SqlServer" : "Sqlite";

    /// <summary>隔离库名(由 identity 派生,合法标识符、稳定;MySQL 与 SqlServer 共用规则)。</summary>
    private static string DbName(string identity) =>
        "tenon_it_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();

    /// <summary>解析连接串:SQLite 指向文件;MySQL/SqlServer 先建库(幂等)再返回带 Database 的连接串。</summary>
    public static string ConnectionString(string identity, string sqliteFile)
    {
        if (UseMySql)
        {
            var db = DbName(identity);
            ExecMySql($"CREATE DATABASE IF NOT EXISTS `{db}` CHARACTER SET utf8mb4;");
            return $"{MySqlBase.TrimEnd(';')};Database={db};";
        }
        if (UseSqlServer)
        {
            var db = DbName(identity);
            // CREATE DATABASE 须为批次内唯一语句,IF 是控制流不算另一条语句,该惯用法可用。
            ExecSqlServer($"IF DB_ID(N'{db}') IS NULL CREATE DATABASE [{db}];");
            return $"{SqlServerBase.TrimEnd(';')};Database={db};";
        }
        return $"Data Source={sqliteFile}";
    }

    /// <summary>清理:SQLite 删文件;MySQL/SqlServer 删库(尽力而为)。</summary>
    public static void Cleanup(string identity, string sqliteFile)
    {
        if (UseMySql)
            try { ExecMySql($"DROP DATABASE IF EXISTS `{DbName(identity)}`;"); } catch { /* 尽力而为 */ }
        else if (UseSqlServer)
            try
            {
                // 释放连接池对目标库的占用,否则活动连接会阻塞 DROP;再断开其余会话后删库。
                SqlConnection.ClearAllPools();
                var db = DbName(identity);
                ExecSqlServer($"IF DB_ID(N'{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END");
            }
            catch { /* 尽力而为 */ }
        else
            try { if (File.Exists(sqliteFile)) File.Delete(sqliteFile); } catch { /* 尽力而为 */ }
    }

    private static void ExecMySql(string sql)
    {
        using var conn = new MySqlConnection(MySqlBase);   // 连服务器(不指定 Database)以建/删库
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void ExecSqlServer(string sql)
    {
        using var conn = new SqlConnection(SqlServerBase);   // 连 master(不指定 Database)以建/删库
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
