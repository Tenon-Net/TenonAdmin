using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ICaptchaService"/> 默认实现。明文进缓存(2 分钟 TTL),校验时<b>先移除再比对</b>——
/// 无论对错该票据都作废,杜绝同一票据重放或多次猜测。
/// </summary>
public class CaptchaService(
    IEnumerable<ICaptchaProvider> providers,
    ICacheProvider cache,
    IConfigService config,
    AdminSecurityOptions security) : ICaptchaService
{
    /// <summary>验证码启用开关配置键(GroupCode=security;后端强制执行时读此键,改值即时生效)。</summary>
    internal const string KEY_ENABLED = "sys.security.captcha.enabled";

    /// <summary>验证码类型配置键(char/path/math 或消费方自注册类型;改值即时生效,下一次签发即换)。</summary>
    internal const string KEY_TYPE = "sys.security.captcha.type";

    // ponytail: 票据有效期固定 2 分钟(够人看清并输入)。要可配再提到 AdminCaptchaOptions,当前无此需求。
    private static readonly TimeSpan TTL = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    public virtual async Task<CaptchaOutput> IssueAsync()
    {
        var provider = await ResolveProviderAsync();
        var captcha = provider.Generate();
        var id = Guid.CreateVersion7().ToString("N");   // 票据 Id:时间有序、不可猜
        await cache.SetAsync(CacheKeys.Captcha(id), captcha.Code, TTL);
        return new CaptchaOutput { CaptchaId = id, Svg = captcha.Svg, Type = provider.Type };
    }

    /// <summary>
    /// 按配置 <c>sys.security.captcha.type</c> 选生成器(缺失回退 Options.Captcha.Type,再回退首个已注册)。
    /// 校验与类型无关(各生成器答案均为字符串,一次性比对),故选型只影响签发。覆写本步可换选型策略(如按租户/IP)。
    /// </summary>
    protected virtual async Task<ICaptchaProvider> ResolveProviderAsync()
    {
        var type = await config.GetValueByKeyAsync(KEY_TYPE);
        if (string.IsNullOrWhiteSpace(type)) type = security.Captcha.Type;
        return providers.FirstOrDefault(p => string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase))
            ?? providers.First();
    }

    /// <inheritdoc />
    public virtual async Task ValidateAsync(string? captchaId, string? code)
    {
        // 是否强制校验先读 SysConfig(改值即时生效),缺失/解析失败回退 Options 默认。
        // 历史 Level3 总档强制验证码;产品路径只认 Captcha:Enabled / SysConfig
        var enabled = bool.TryParse(await config.GetValueByKeyAsync(KEY_ENABLED), out var e) ? e : security.Captcha.Enabled;
        if (security.IsLegacyLevel3Profile)
            enabled = true;
        if (!enabled) return;   // 未启用:直通(登录不校验验证码)

        AdminException.ThrowIf(string.IsNullOrEmpty(captchaId) || string.IsNullOrEmpty(code), ErrorCode.CaptchaExpired);

        // 原子取删:并发携同一 captchaId 时只有一个调用取到非空值,杜绝单张验证码放大成 N 次猜测(§14 一次性)
        var stored = await cache.GetAndRemoveAsync<string>(CacheKeys.Captcha(captchaId!));

        AdminException.ThrowIf(stored is null, ErrorCode.CaptchaExpired);
        AdminException.ThrowIf(!string.Equals(stored, code, StringComparison.OrdinalIgnoreCase), ErrorCode.CaptchaWrong);
    }
}
