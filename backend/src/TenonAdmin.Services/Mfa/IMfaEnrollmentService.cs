namespace TenonAdmin.Services;

/// <summary>
/// TOTP 自助绑定、恢复码、管理员清除 MFA(ADR 0006)。
/// 种子经 <see cref="Core.ISecretProtector"/> 加密落库;恢复码只存哈希。
/// 不提供部署 InitGrant / 绑定邀请 / 紧急授权产品路径。
/// </summary>
public interface IMfaEnrollmentService
{
    /// <summary>
    /// 自助绑定启动:账号 + 当前密码 → 生成 seed(缓存暂存)与 otpauth URI。
    /// 无密码 / 密码错误 → <see cref="Core.ErrorCode.MfaBindPasswordRequired"/> 或 <see cref="Core.ErrorCode.PasswordWrong"/>。
    /// 须 <c>Security:Totp:Enabled</c>(或历史 Profile=Level3)。
    /// </summary>
    Task<TotpBindStartOutput> StartBindAsync(TotpBindStartInput input);

    /// <summary>
    /// 绑定完成:校验首个 TOTP 码,写加密 seed、恢复码哈希、标记 TotpEnabled。
    /// 恢复码明文仅返回一次。
    /// </summary>
    Task<TotpBindCompleteOutput> CompleteBindAsync(TotpBindCompleteInput input);

    /// <summary>
    /// 使用恢复码:验账密+码 → 吊销全部会话、清除 TOTP,须重新自助绑定。
    /// </summary>
    Task UseRecoveryCodeAsync(TotpRecoveryInput input);

    /// <summary>
    /// 管理员清除目标用户 MFA(不发邀请)。目标用户之后自助重绑。
    /// 操作人须超管,或持有清除权限;TOTP 开启时操作人须已绑定。
    /// </summary>
    Task ClearUserMfaAsync(long targetUserId, long operatorUserId);
}
