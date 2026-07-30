namespace TenonAdmin.Services;

/// <summary>
/// TOTP 绑定邀请、绑定、恢复码、超管 MFA 重置。
/// 种子仅经 <see cref="Core.ISecretProtector"/> 加密落库;恢复码/邀请只存哈希。
/// </summary>
public interface IMfaEnrollmentService
{
    /// <summary>
    /// 已完成 TOTP 的管理员为目标用户发放 15 分钟一次性绑定邀请。
    /// 同用户未使用旧邀请自动作废。返回 bearer 明文一次。
    /// </summary>
    Task<TotpBindInviteOutput> IssueBindInviteAsync(long targetUserId, long issuedByUserId);

    /// <summary>撤销未使用邀请。</summary>
    Task RevokeBindInviteAsync(long inviteId, long operatorUserId);

    /// <summary>
    /// 绑定启动:校验邀请或 InitGrant + 当前密码,生成 seed(缓存暂存),返回 otpauth URI。
    /// 无密码 / 密码错误 → <see cref="Core.ErrorCode.MfaBindPasswordRequired"/> 或 <see cref="Core.ErrorCode.PasswordWrong"/>。
    /// </summary>
    Task<TotpBindStartOutput> StartBindAsync(TotpBindStartInput input);

    /// <summary>
    /// 绑定完成:校验首个 TOTP 码,写加密 seed、10 个恢复码哈希、标记 TotpEnabled。
    /// 恢复码明文仅返回一次。
    /// </summary>
    Task<TotpBindCompleteOutput> CompleteBindAsync(TotpBindCompleteInput input);

    /// <summary>
    /// 使用恢复码:验账密+码 → 吊销全部会话、清除 TOTP seed/Enabled、废旧恢复码,强制重新绑定。
    /// </summary>
    Task UseRecoveryCodeAsync(TotpRecoveryInput input);

    /// <summary>
    /// 超级管理员 MFA 重置:peer 批准或紧急授权 → 清除目标 MFA + 发重新绑定邀请。
    /// 记最高级安全日志。
    /// </summary>
    Task<TotpResetOutput> ResetSuperAdminMfaAsync(TotpSuperAdminResetInput input, long? operatorUserId);

    /// <summary>
    /// 唯一超管紧急 MFA 恢复(匿名可达):EmergencyGrant + 账号密码 → 清 MFA 并签发重绑邀请。
    /// </summary>
    Task<TotpResetOutput> EmergencyResetSoleSuperAdminAsync(TotpEmergencyResetInput input);
}
