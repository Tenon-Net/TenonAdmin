using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>刷新令牌状态(设计 §15)。</summary>
public enum RefreshTokenStatus
{
    /// <summary>活跃:可用于换发</summary>
    Active = 1,

    /// <summary>已轮换:换发新令牌后旧令牌置此。<b>再被出示 = 重放</b>,触发整会话吊销(复用检测)。</summary>
    Used = 2,

    /// <summary>已吊销:随会话登出/强退作废</summary>
    Revoked = 3,
}

/// <summary>
/// 刷新令牌(设计 §14/§15)——服务端只存<b>哈希</b>不存明文(高熵随机串,SHA-256 足够,无需 PBKDF2)。
/// 轮换:每次换发把旧令牌置 <see cref="RefreshTokenStatus.Used"/> 并签发新令牌;旧令牌再现即判重放。
/// </summary>
[SugarTable("sys_refresh_token", TableDescription = "刷新令牌")]
[SugarIndex("idx_sys_refresh_token_hash", nameof(TokenHash), OrderByType.Asc, IsUnique = true)]
public class SysRefreshToken : BaseEntity
{
    /// <summary>所属会话(与 <see cref="SysSession.SessionId"/> 对应,吊销时按它连坐)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "所属会话标识")]
    public string SessionId { get; set; } = "";

    [SugarColumn(ColumnDescription = "用户 Id")]
    public long UserId { get; set; }

    /// <summary>令牌明文的 SHA-256(十六进制);查存按它比对,库里绝无明文</summary>
    [SugarColumn(Length = 128, ColumnDescription = "令牌哈希(SHA-256 hex)")]
    public string TokenHash { get; set; } = "";

    [SugarColumn(ColumnDescription = "过期时刻")]
    public DateTime ExpiresAt { get; set; }

    [SugarColumn(ColumnDescription = "状态(活跃/已轮换/已吊销)")]
    public RefreshTokenStatus Status { get; set; } = RefreshTokenStatus.Active;
}
