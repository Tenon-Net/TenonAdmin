namespace TenonAdmin.Core;

/// <summary>数据库配置(对应 appsettings 的 TenonAdmin:Database)</summary>
public class AdminDatabaseOptions
{
    /// <summary>Sqlite | MySql | SqlServer | PostgreSQL</summary>
    public string DbType { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=./data/admin.db";

    /// <summary>首启是否 CodeFirst 自动建表</summary>
    public bool EnableCodeFirst { get; set; } = true;

    /// <summary>首启是否写种子</summary>
    public bool EnableSeed { get; set; } = true;
}
