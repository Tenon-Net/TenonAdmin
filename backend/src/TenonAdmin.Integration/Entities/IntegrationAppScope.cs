using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 接入应用数据范围绑定(<c>itg_app_scope</c>):每应用每范围策略一行。无行 = 未绑定 = 调用声明该范围的端点即 403;
/// <see cref="AllValues"/> 为管理员显式授予的全量,绝不由缺省推出。
/// </summary>
[SugarTable("itg_app_scope", TableDescription = "接入应用数据范围绑定")]
[SugarIndex("uk_itg_app_scope", nameof(AppId), OrderByType.Asc, nameof(ScopeKey), OrderByType.Asc, IsUnique = true)]
public class IntegrationAppScope : AuditEntity
{
    /// <summary>接入应用 Id。</summary>
    [SugarColumn(ColumnDescription = "接入应用 Id")]
    public long AppId { get; set; }

    /// <summary>范围策略键。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "范围策略键")]
    public string ScopeKey { get; set; } = "";

    /// <summary>显式授予该维度全部数据。</summary>
    [SugarColumn(ColumnDescription = "是否全部数据(显式授予)")]
    public bool AllValues { get; set; }

    /// <summary>绑定值 JSON 数组(字符串);<see cref="AllValues"/> 为真时为空数组。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, ColumnDescription = "绑定值 JSON 数组")]
    public string ValuesJson { get; set; } = "[]";
}
