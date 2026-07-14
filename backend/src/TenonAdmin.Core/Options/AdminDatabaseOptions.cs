namespace TenonAdmin.Core;

/// <summary>数据库配置(对应 appsettings 的 TenonAdmin:Database)</summary>
public class AdminDatabaseOptions
{
    /// <summary>Sqlite | MySql | SqlServer | PostgreSQL</summary>
    public string DbType { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=./data/admin.db";

    /// <summary>首启是否 CodeFirst 自动建表</summary>
    public bool EnableCodeFirst { get; set; } = true;

    /// <summary>
    /// 生产环境是否允许 CodeFirst 自动建表/改表(设计 §12/§4.1 建表安全闸门)。
    /// <b>默认 false</b>:生产库通常由 DBA 手工维护,应用不应擅自 ALTER 表结构。即使 <see cref="EnableCodeFirst"/> 为 true,
    /// 生产环境(<c>IHostEnvironment.IsProduction()</c>)也需本项显式为 true 才建表;非生产环境不受此约束。
    /// </summary>
    public bool EnableCodeFirstInProduction { get; set; }

    /// <summary>首启是否写种子</summary>
    public bool EnableSeed { get; set; } = true;

    /// <summary>
    /// 慢 SQL 告警阈值(毫秒):执行耗时 ≥ 本值的语句,连同 SQL 与参数打一条 <c>Warning</c>。
    /// <para><b>≤ 0 = 关闭</b>(只保留失败 SQL 的 <c>Error</c> 日志,那条不受本项控制 —— 查询失败却打不出 SQL,
    /// 线上就没法查了,不给关的开关)。</para>
    /// <para>默认 1000ms:够慢才值得看。调小(如 1)可用来观察全部语句,但生产上会把日志淹掉。</para>
    /// </summary>
    public int SlowSqlMillis { get; set; } = 1000;
}
