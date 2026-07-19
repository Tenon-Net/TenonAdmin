using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 用户外部身份绑定(设计批次 D)。一行 = 某本地用户绑定了某 provider 下的某外部身份。
/// <para><c>(Provider, Subject)</c> 全局唯一(一个外部身份只归一个本地账号);另按 <c>UserId</c> 建普通索引供"我的绑定"查询。
/// 继承 <see cref="BaseEntity"/>:软删 + 审计 + 雪花 Id 自带,非机构范围表(与 SysUser/SysUserRole 一致)。</para>
/// </summary>
[SugarTable("sys_user_external", TableDescription = "用户外部身份绑定")]
[SugarIndex("idx_sys_user_external", nameof(Provider), OrderByType.Asc, nameof(Subject), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_sys_user_external_user", nameof(UserId), OrderByType.Asc)]
public class SysUserExternal : BaseEntity
{
    [SugarColumn(ColumnDescription = "本地用户 Id")]
    public long UserId { get; set; }

    [SugarColumn(Length = 32, ColumnDescription = "provider 码(oidc/keycloak/wecom/dingtalk…)")]
    public string Provider { get; set; } = "";

    [SugarColumn(Length = 256, ColumnDescription = "外部唯一标识(OIDC sub / 企业微信 userid / 钉钉 unionid)")]
    public string Subject { get; set; } = "";

    [SugarColumn(Length = 128, ColumnDescription = "绑定时的外部显示名(仅备查)", IsNullable = true)]
    public string? DisplayName { get; set; }
}
