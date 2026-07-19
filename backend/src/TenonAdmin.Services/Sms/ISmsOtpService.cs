namespace TenonAdmin.Services;

/// <summary>
/// 短信验证码服务(设计 §14 登录加固):签发/校验一次性短信码 + MFA 挑战票据生命周期。
/// 码与票据全走 <see cref="Core.ICacheProvider"/>(TTL 过期、原子取删单次消费),零 DDL;
/// 发送经 <see cref="Core.ISmsSender"/>(默认日志通道,消费方前置注册真实厂商实现)。
/// </summary>
public interface ISmsOtpService
{
    /// <summary>用途:登录二次验证(码按挑战 Id 存,绑定已过密码校验的用户)</summary>
    const string PURPOSE_MFA = "mfa";

    /// <summary>用途:验证码免密登录(码按规范化手机号存)</summary>
    const string PURPOSE_LOGIN = "login";

    /// <summary>短信二次验证是否启用(DB 键 <c>sys.security.mfa.enabled</c> 优先,Options 兜底)</summary>
    Task<bool> IsMfaEnabledAsync();

    /// <summary>短信免密登录是否启用(DB 键 <c>sys.security.smsLogin.enabled</c> 优先,Options 兜底)</summary>
    Task<bool> IsLoginEnabledAsync();

    /// <summary>
    /// 签发并发送验证码:过发送闸门(冷却 + 日上限,超限抛 <see cref="Core.ErrorCode.TooManyRequests"/>),
    /// 生成定长随机数字码存缓存(TTL 内有效、单次消费),经 <see cref="Core.ISmsSender"/> 发出。
    /// </summary>
    /// <param name="purpose"><see cref="PURPOSE_MFA"/> | <see cref="PURPOSE_LOGIN"/></param>
    /// <param name="cacheKey">码的存取键:MFA 用挑战 Id,免密登录用规范化手机号</param>
    /// <param name="phone">收码手机号</param>
    Task<SmsSendOutput> IssueAsync(string purpose, string cacheKey, string phone);

    /// <summary>
    /// 防枚举陪跑签发:走同一发送闸门、设同样冷却、返回同形出参,但<b>不存码不发短信</b>——
    /// 手机号未命中唯一启用用户时调用,使响应与真实路径不可区分(§14 防账号枚举)。
    /// </summary>
    Task<SmsSendOutput> PretendIssueAsync(string phone);

    /// <summary>
    /// 校验并消费验证码:缺失/过期/已消费抛 <see cref="Core.ErrorCode.SmsCodeExpired"/>;
    /// 错码计次抛 <see cref="Core.ErrorCode.SmsCodeWrong"/>(args 带 attemptsLeft),达上限作废该码;
    /// 对码原子取删(并发同码只有一个成功)。
    /// </summary>
    Task VerifyAsync(string purpose, string cacheKey, string code);

    /// <summary>创建 MFA 挑战票据(值 = 已过密码校验的 userId,TTL 同验证码),返回挑战 Id。</summary>
    Task<string> CreateMfaChallengeAsync(long userId);

    /// <summary>查看挑战对应的 userId(不消费,供重发短信用);缺失/过期返回 0。</summary>
    Task<long> GetMfaChallengeAsync(string challengeId);

    /// <summary>原子消费挑战票据,返回 userId;缺失/过期/已消费返回 0。</summary>
    Task<long> ConsumeMfaChallengeAsync(string challengeId);
}
