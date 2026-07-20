namespace TenonAdmin.Core;

/// <summary>生成的验证码:服务端保存 <see cref="Code"/> 校验、只把 <see cref="Svg"/> 发给前端。</summary>
/// <param name="Code">验证码明文(存服务端缓存,不下发)</param>
/// <param name="Svg">
/// 渲染后的 SVG 图(下发前端展示)。前端直接以 <c>dangerouslySetInnerHTML</c> / <c>v-html</c> 注入 DOM,
/// <b>不做任何净化</b> —— 所以 <b>SVG 内容不得嵌入任何不可信输入</b>(请求参数、用户资料等)。
/// 验证码端点是**登录前匿名**可达的,一旦把外部输入拼进这里就是 pre-auth XSS。
/// 内置三个 provider 的 SVG 全由固定字符集 + 服务端 RNG / 算式自生成,零请求输入入串;
/// 自定义 <see cref="ICaptchaProvider"/> 必须守住这条契约。
/// </param>
public record Captcha(string Code, string Svg);

/// <summary>
/// 验证码生成扩展点(设计 §5 扩展点表)。内置三选:<c>char</c>(字符 SVG)、<c>path</c>(描边字形,明文不入标记、更抗爬)、
/// <c>math</c>(算术)。用哪种由配置 <c>sys.security.captcha.type</c> 运行时选。
/// 想要图片/滑块/行为验证码(如接 SixLabors/Skia 的滑块),实现本接口、声明自己的 <see cref="Type"/> 前置注册,
/// 再把配置类型改成它即整体接管——无需改内核。
/// </summary>
public interface ICaptchaProvider
{
    /// <summary>本生成器的类型标识(如 <c>char</c>/<c>path</c>/<c>math</c>);配置按此选型。默认 <c>char</c>,兼容旧实现。</summary>
    string Type => "char";

    /// <summary>生成一枚验证码(明文 + SVG)。明文由上层存缓存并校验,SVG 下发前端。</summary>
    Captcha Generate();
}
