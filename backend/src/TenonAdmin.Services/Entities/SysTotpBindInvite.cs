using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// TOTP 绑定邀请(等保三级应用安全一期)——短时、一次性 bearer 凭据。
/// 目标用户还须验证当前密码后才可写入 TOTP seed。默认 15 分钟生命周期,可撤销。
/// </summary>
[SugarTable("sys_totp_bind_invite", TableDescription = "TOTP 绑定邀请")]
[SugarIndex("idx_sys_totp_invite_hash", nameof(TokenHash), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_sys_totp_invite_user", nameof(UserId), OrderByType.Asc)]
public class SysTotpBindInvite : BaseEntity
{
    /// <summary>邀请令牌哈希(明文只交付一次)</summary>
    [SugarColumn(Length = 128, ColumnDescription = "邀请令牌哈希")]
    public string TokenHash { get; set; } = "";

    /// <summary>目标用户 Id</summary>
    [SugarColumn(ColumnDescription = "用户 Id")]
    public long UserId { get; set; }

    /// <summary>过期时刻</summary>
    [SugarColumn(ColumnDescription = "过期时刻")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>消费时刻;null = 未使用</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "消费时刻")]
    public DateTime? ConsumedAt { get; set; }

    /// <summary>撤销时刻;null = 未撤销</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "撤销时刻")]
    public DateTime? RevokedAt { get; set; }

    /// <summary>发放人用户 Id(审计)</summary>
    [SugarColumn(ColumnDescription = "发放人用户 Id")]
    public long IssuedByUserId { get; set; }
}
