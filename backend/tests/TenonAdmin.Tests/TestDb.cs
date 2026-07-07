using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace TenonAdmin.Tests;

/// <summary>
/// 集成测试的数据库选择(§8 数据库矩阵)。默认 SQLite(本地 <c>dotnet test</c> 不变);CI 的 MySQL 矩阵腿
/// 通过环境变量切到 MySQL:<c>TENON_TEST_DBTYPE=MySql</c> + <c>TENON_TEST_MYSQL=</c>(不含 Database 的服务器连接串)。
/// <para>MySQL 隔离:库名由 <c>identity</c> 确定性派生——同 identity → 同库(支持"同库二次启动"的幂等用例),
/// 不同 identity → 各自独立库。建库/删库经原始 <see cref="MySqlConnection"/>(SqlSugar 不负责建库)。</para>
/// </summary>
internal static class TestDb
{
    public static bool UseMySql =>
        string.Equals(Environment.GetEnvironmentVariable("TENON_TEST_DBTYPE"), "MySql", StringComparison.OrdinalIgnoreCase);

    private static string MySqlBase =>
        Environment.GetEnvironmentVariable("TENON_TEST_MYSQL")
        ?? "Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;";

    /// <summary>当前腿的 DbType 字符串(直接喂给 <c>AdminDatabaseOptions.DbType</c> / 配置)。</summary>
    public static string DbType => UseMySql ? "MySql" : "Sqlite";

    /// <summary>MySQL 库名(由 identity 派生,合法标识符、稳定)。</summary>
    private static string MySqlDbName(string identity) =>
        "tenon_it_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();

    /// <summary>解析连接串:SQLite 指向文件;MySQL 先建库(幂等)再返回带 Database 的连接串。</summary>
    public static string ConnectionString(string identity, string sqliteFile)
    {
        if (!UseMySql) return $"Data Source={sqliteFile}";
        var db = MySqlDbName(identity);
        Exec($"CREATE DATABASE IF NOT EXISTS `{db}` CHARACTER SET utf8mb4;");
        return $"{MySqlBase.TrimEnd(';')};Database={db};";
    }

    /// <summary>清理:SQLite 删文件;MySQL 删库(尽力而为)。</summary>
    public static void Cleanup(string identity, string sqliteFile)
    {
        if (UseMySql)
            try { Exec($"DROP DATABASE IF EXISTS `{MySqlDbName(identity)}`;"); } catch { /* 尽力而为 */ }
        else
            try { if (File.Exists(sqliteFile)) File.Delete(sqliteFile); } catch { /* 尽力而为 */ }
    }

    private static void Exec(string sql)
    {
        using var conn = new MySqlConnection(MySqlBase);   // 连服务器(不指定 Database)以建/删库
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
