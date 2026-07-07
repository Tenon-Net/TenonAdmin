using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ICaptchaService"/> 默认实现。明文进缓存(2 分钟 TTL),校验时<b>先移除再比对</b>——
/// 无论对错该票据都作废,杜绝同一票据重放或多次猜测。
/// </summary>
public class CaptchaService(
    ICaptchaProvider provider,
    ICacheProvider cache,
    AdminSecurityOptions security) : ICaptchaService
{
    // ponytail: 票据有效期固定 2 分钟(够人看清并输入)。要可配再提到 AdminCaptchaOptions,当前无此需求。
    private static readonly TimeSpan TTL = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    public bool Enabled => security.Captcha.Enabled;

    /// <inheritdoc />
    public virtual async Task<CaptchaOutput> IssueAsync()
    {
        var captcha = provider.Generate();
        var id = Guid.CreateVersion7().ToString("N");   // 票据 Id:时间有序、不可猜
        await cache.SetAsync(CacheKeys.Captcha(id), captcha.Code, TTL);
        return new CaptchaOutput { CaptchaId = id, Svg = captcha.Svg };
    }

    /// <inheritdoc />
    public virtual async Task ValidateAsync(string? captchaId, string? code)
    {
        if (!Enabled) return;   // 未启用:直通(登录不校验验证码)

        AdminException.ThrowIf(string.IsNullOrEmpty(captchaId) || string.IsNullOrEmpty(code), ErrorCode.CaptchaExpired);

        // 原子取删:并发携同一 captchaId 时只有一个调用取到非空值,杜绝单张验证码放大成 N 次猜测(§14 一次性)
        var stored = await cache.GetAndRemoveAsync<string>(CacheKeys.Captcha(captchaId!));

        AdminException.ThrowIf(stored is null, ErrorCode.CaptchaExpired);
        AdminException.ThrowIf(!string.Equals(stored, code, StringComparison.OrdinalIgnoreCase), ErrorCode.CaptchaWrong);
    }
}
