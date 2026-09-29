using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 接入应用授权(<c>itg_app_grant</c>):应用 × 开放端点规范化路由权限码,一行一码,默认拒绝(无行即无权)。
/// 与后台角色授权(<c>sys_role_menu</c>)完全分开——应用授权不影响任何用户,用户授权也不会授给应用。
/// </summary>
[SugarTable("itg_app_grant", TableDescription = "接入应用授权")]
[SugarIndex("uk_itg_app_grant", nameof(AppId), OrderByType.Asc, nameof(Permission), OrderByType.Asc, IsUnique = true)]
public class IntegrationAppGrant : AuditEntity
{
    /// <summary>接入应用 Id。</summary>
    [SugarColumn(ColumnDescription = "接入应用 Id")]
    public long AppId { get; set; }

    /// <summary>规范化路由权限码 <c>VERB:/api/open/v{n}/...</c>。</summary>
    [SugarColumn(Length = 256, ColumnDescription = "开放端点权限码")]
    public string Permission { get; set; } = "";
}
