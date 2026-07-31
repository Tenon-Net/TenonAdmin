namespace TenonAdmin.Services;

/// <summary>
/// 密码通过后的 TOTP 二次验证挑战(与短信 MFA 挑战同形)。
/// AuthService 在 Level3/强制 MFA 路径可调用本接口,抛 <see cref="Core.ErrorCode.TotpRequired"/> 信令。
/// </summary>
public interface IMfaChallengeService
{
    /// <summary>创建挑战票据(值=已过密码校验的 userId),返回 challengeId。</summary>
    Task<string> CreateChallengeAsync(long userId);

    /// <summary>查看挑战对应 userId(不消费);缺失/过期返回 0。</summary>
    Task<long> GetChallengeAsync(string challengeId);

    /// <summary>
    /// 校验 TOTP 并消费挑战。
    /// 码错或挑战失效 → <see cref="Core.ErrorCode.TotpWrong"/>;
    /// 用户未绑定 → <see cref="Core.ErrorCode.TotpNotBound"/>。
    /// </summary>
    /// <returns>通过校验的 userId</returns>
    Task<long> VerifyAndConsumeAsync(string challengeId, string totpCode);
}
