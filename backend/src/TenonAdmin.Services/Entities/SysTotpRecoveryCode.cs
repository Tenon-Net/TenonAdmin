using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// TOTP 恢复码(等保三级应用安全一期)——明文仅展示一次,服务端只存哈希。
/// 每码一次性;使用后强制重新绑定 TOTP 并吊销该用户全部会话。
/// </summary>
[SugarTable("sys_totp_recovery_code", TableDescription = "TOTP 恢复码")]
[SugarIndex("idx_sys_totp_rc_user", nameof(UserId), OrderByType.Asc)]
public class SysTotpRecoveryCode : BaseEntity
{
    /// <summary>所属用户 Id</summary>
    [SugarColumn(ColumnDescription = "用户 Id")]
    public long UserId { get; set; }

    /// <summary>恢复码哈希(高熵;SHA-256 hex 或与口令同 IPasswordHasher 格式)</summary>
    [SugarColumn(Length = 128, ColumnDescription = "恢复码哈希")]
    public string CodeHash { get; set; } = "";

    /// <summary>使用时刻;null = 未使用</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "使用时刻")]
    public DateTime? UsedAt { get; set; }
}
