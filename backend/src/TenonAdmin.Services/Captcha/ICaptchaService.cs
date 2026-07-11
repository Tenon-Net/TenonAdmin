namespace TenonAdmin.Services;

/// <summary>
/// 验证码服务(设计 §3.2/§14)——签发(生成 + 缓存明文 + 下发 SVG)与校验(一次性消费)。
/// 是否强制校验由配置键 <c>sys.security.captcha.enabled</c> 运行时驱动(读穿透缓存,改值即时生效),
/// 关闭时 <see cref="ValidateAsync"/> 直通;前端经站点信息 <c>SiteInfoOutput.CaptchaEnabled</c> 决定是否展示验证码。
/// </summary>
public interface ICaptchaService
{
    /// <summary>签发一枚验证码:生成明文存缓存(短 TTL),返回票据 Id + SVG。</summary>
    Task<CaptchaOutput> IssueAsync();

    /// <summary>
    /// 校验验证码并<b>一次性消费</b>(无论对错都作废该票据,防重放/暴力猜)。
    /// 未启用则直通;缺失/过期抛 <see cref="Core.ErrorCode.CaptchaExpired"/>,不匹配抛 <see cref="Core.ErrorCode.CaptchaWrong"/>。
    /// </summary>
    Task ValidateAsync(string? captchaId, string? code);
}
