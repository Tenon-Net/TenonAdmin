namespace TenonAdmin.Core;

/// <summary>生成的验证码:服务端保存 <see cref="Code"/> 校验、只把 <see cref="Svg"/> 发给前端。</summary>
/// <param name="Code">验证码明文(存服务端缓存,不下发)</param>
/// <param name="Svg">渲染后的 SVG 图(下发前端展示)</param>
public record Captcha(string Code, string Svg);

/// <summary>
/// 验证码生成扩展点(设计 §5 扩展点表)。默认实现 <c>SvgCaptchaProvider</c>——纯字符串 SVG、零绘图依赖、跨平台。
/// 想要图片/滑块/行为验证码,实现本接口并前置注册即整体替换。
/// </summary>
public interface ICaptchaProvider
{
    /// <summary>生成一枚验证码(明文 + SVG)。明文由上层存缓存并校验,SVG 下发前端。</summary>
    Captcha Generate();
}
