using System.Security.Cryptography;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISmsOtpService"/> 默认实现(成法同 <see cref="CaptchaService"/>:明文短 TTL 进缓存、
/// 原子取删单次消费;开关 DB 优先 Options 兜底,改值即时生效)。步骤 virtual,可单独覆写
/// (如换码长策略、接自有风控闸门)。
/// </summary>
public class SmsOtpService(
    ISmsSender smsSender,
    ICacheProvider cache,
    IConfigService config,
    AdminSecurityOptions security) : ISmsOtpService
{
    /// <summary>短信二次验证开关配置键(GroupCode=security;改值即时生效)</summary>
    internal const string KEY_MFA_ENABLED = "sys.security.mfa.enabled";

    /// <summary>短信免密登录开关配置键(GroupCode=security;改值即时生效)</summary>
    internal const string KEY_LOGIN_ENABLED = "sys.security.smsLogin.enabled";

    private AdminSmsOtpOptions Opt => security.SmsOtp;

    /// <inheritdoc />
    public virtual async Task<bool> IsMfaEnabledAsync() =>
        bool.TryParse(await config.GetValueByKeyAsync(KEY_MFA_ENABLED), out var e) ? e : Opt.MfaEnabled;

    /// <inheritdoc />
    public virtual async Task<bool> IsLoginEnabledAsync() =>
        bool.TryParse(await config.GetValueByKeyAsync(KEY_LOGIN_ENABLED), out var e) ? e : Opt.LoginEnabled;

    /// <inheritdoc />
    public virtual async Task<SmsSendOutput> IssueAsync(string purpose, string cacheKey, string phone)
    {
        phone = Normalize(phone);
        await EnsureSendAllowedAsync(phone);

        var code = GenerateCode();
        var ttl = TimeSpan.FromSeconds(Opt.TtlSeconds);
        await cache.SetAsync(CacheKeys.SmsCode(purpose, cacheKey), code, ttl);
        await cache.RemoveAsync(CacheKeys.SmsCodeAttempts(purpose, cacheKey));   // 新码新计数
        await smsSender.SendCodeAsync(phone, code, purpose);
        return new SmsSendOutput { ExpiresSeconds = Opt.TtlSeconds, ResendSeconds = Opt.ResendSeconds };
    }

    /// <inheritdoc />
    public virtual async Task<SmsSendOutput> PretendIssueAsync(string phone)
    {
        // 与真实路径同闸门同出参:未命中用户的手机号也吃冷却/日上限,重复请求的表现完全一致(防枚举)
        await EnsureSendAllowedAsync(Normalize(phone));
        return new SmsSendOutput { ExpiresSeconds = Opt.TtlSeconds, ResendSeconds = Opt.ResendSeconds };
    }

    /// <inheritdoc />
    public virtual async Task VerifyAsync(string purpose, string cacheKey, string code)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(code), ErrorCode.SmsCodeExpired);

        var codeKey = CacheKeys.SmsCode(purpose, cacheKey);
        var attemptsKey = CacheKeys.SmsCodeAttempts(purpose, cacheKey);
        var stored = await cache.GetAsync<string>(codeKey);
        AdminException.ThrowIf(stored is null, ErrorCode.SmsCodeExpired);

        if (!string.Equals(stored, code, StringComparison.Ordinal))
        {
            // 错码计次(与码同 TTL);达上限作废该码——单码最多被猜 MaxAttempts 次,重猜只能重新发码(那里有冷却/日上限)
            var tries = await cache.IncrementAsync(attemptsKey, TimeSpan.FromSeconds(Opt.TtlSeconds));
            if (tries >= Opt.MaxAttempts) await cache.RemoveAsync(codeKey);
            throw new AdminException(ErrorCode.SmsCodeWrong,
                new Dictionary<string, object?> { ["attemptsLeft"] = Math.Max(0, Opt.MaxAttempts - tries) });
        }

        // 原子取删:并发携同一码只有一个调用取到非空值(§14 一次性,同验证码成法)
        var consumed = await cache.GetAndRemoveAsync<string>(codeKey);
        AdminException.ThrowIf(consumed is null, ErrorCode.SmsCodeExpired);
        await cache.RemoveAsync(attemptsKey);
    }

    /// <inheritdoc />
    public virtual async Task<string> CreateMfaChallengeAsync(long userId)
    {
        var challengeId = Guid.CreateVersion7().ToString("N");   // 时间有序、不可猜(同验证码票据成法)
        await cache.SetAsync(CacheKeys.MfaChallenge(challengeId), userId, TimeSpan.FromSeconds(Opt.TtlSeconds));
        return challengeId;
    }

    /// <inheritdoc />
    public virtual Task<long> GetMfaChallengeAsync(string challengeId) =>
        cache.GetAsync<long>(CacheKeys.MfaChallenge(challengeId));

    /// <inheritdoc />
    public virtual Task<long> ConsumeMfaChallengeAsync(string challengeId) =>
        cache.GetAndRemoveAsync<long>(CacheKeys.MfaChallenge(challengeId));

    /// <summary>
    /// 发送闸门:冷却期内(距上次发送不足 ResendSeconds)或当日发送达上限即抛
    /// <see cref="ErrorCode.TooManyRequests"/>。日计数把日期编进键——换天换键自动归零(同限流成法)。
    /// </summary>
    protected virtual async Task EnsureSendAllowedAsync(string phone)
    {
        var cooling = await cache.GetAsync<bool>(CacheKeys.SmsCooldown(phone));
        AdminException.ThrowIf(cooling, ErrorCode.TooManyRequests,
            new Dictionary<string, object?> { ["retryAfterSeconds"] = Opt.ResendSeconds });

        if (Opt.DailySendLimitPerPhone > 0)
        {
            var day = DateTime.Now.ToString("yyyyMMdd");
            var count = await cache.IncrementAsync(CacheKeys.SmsDailyCount(phone, day), TimeSpan.FromHours(24));
            AdminException.ThrowIf(count > Opt.DailySendLimitPerPhone, ErrorCode.TooManyRequests);
        }

        await cache.SetAsync(CacheKeys.SmsCooldown(phone), true, TimeSpan.FromSeconds(Opt.ResendSeconds));
    }

    /// <summary>密码学随机定长数字码(<c>RandomNumberGenerator</c>,非 <c>Random</c>——验证码可猜即失效)</summary>
    protected virtual string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, Opt.CodeLength)).ToString($"D{Opt.CodeLength}");

    /// <summary>手机号规范化(仅去空白;不做区号/格式假设,格式校验属前端与消费方)</summary>
    protected static string Normalize(string phone) => phone.Trim();
}
