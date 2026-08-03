namespace TenonAdmin.Core;

/// <summary>
/// 副库连接配置(对应 appsettings 的 <c>TenonAdmin:AdditionalDatabases[]</c>)。
/// <para>内核只负责把连接挂进同一 <c>SqlSugarScope</c> 并按项 opt-in 钩子;
/// 副库建表/种子/仓储路由不在本选项范围内(见 issue #28 grilled plan)。</para>
/// </summary>
public class AdminDatabaseConnectionOptions
{
    /// <summary>
    /// SqlSugar <c>ConfigId</c>。必须非空、全局唯一,且不得为保留值 <c>TenonAdmin</c>(主库)。
    /// </summary>
    public string ConfigId { get; set; } = "";

    /// <summary>Sqlite | MySql | SqlServer | PostgreSQL</summary>
    public string DbType { get; set; } = "Sqlite";

    /// <summary>
    /// 连接串。SQLite 相对 <c>Data Source</c> 在装配时按 ContentRoot 解析为绝对路径,并 Ensure 父目录。
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// 是否挂软删全局过滤器(<c>ISoftDelete → IsDelete == false</c>)。
    /// <para><b>默认 false</b>:遗留库/外部库常无 IsDelete 列,误开会查炸。</para>
    /// </summary>
    public bool ApplySoftDeleteFilter { get; set; }

    /// <summary>
    /// 是否挂机构数据范围过滤器(<c>IOrgScoped</c>)。
    /// <para><b>默认 false</b>:外部库通常不按 CreateOrgId 建模。</para>
    /// </summary>
    public bool ApplyDataScopeFilter { get; set; }

    /// <summary>
    /// 是否挂审计字段 AOP(雪花 Id / CreateTime / 操作人 / CreateOrgId 等)。
    /// <para><b>默认 false</b>:列结构与内核基类不一致时勿开。</para>
    /// </summary>
    public bool ApplyAuditAop { get; set; }

    /// <summary>
    /// 慢 SQL 告警阈值(毫秒)。语义同主库 <see cref="AdminDatabaseOptions.SlowSqlMillis"/>:
    /// ≤0 关闭(默认 0);失败 SQL 的 Error 日志不受本项控制、始终开启。
    /// </summary>
    public int SlowSqlMillis { get; set; }
}
