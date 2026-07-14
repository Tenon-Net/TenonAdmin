using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ICaptchaProvider"/> 默认实现:纯字符串拼 SVG,零绘图依赖、跨平台(设计 §5/§3.2)。
/// 验证码明文用加密安全随机源生成(去混淆字符);SVG 加干扰线 + 逐字旋转抖动。
/// </summary>
// ponytail: <text> 渲染的 SVG 是机器可读的(能从 <text> 直接抠出字符),防人不防机——这是"零依赖默认"的已知天花板。
//           要真正抗自动识别(路径化/扭曲/滑块/行为),实现 ICaptchaProvider 前置替换即可,内核不背这个重量。
public sealed class SvgCaptchaProvider : ICaptchaProvider
{
    private const string CHARSET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // 去掉 0/O、1/I/L 等易混淆
    private const int LENGTH = 4;

    /// <inheritdoc />
    public string Type => "char";

    public Captcha Generate()
    {
        // 明文:加密安全随机(不用 Random,验证码强度相关)
        var code = string.Create(LENGTH, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = CHARSET[RandomNumberGenerator.GetInt32(CHARSET.Length)];
        });
        return new Captcha(code, Render(code));
    }

    /// <summary>把验证码渲染成 SVG。视觉抖动(位置/颜色/旋转)是非安全项,用普通 <see cref="Random"/> 即可。</summary>
    private static string Render(string code)
    {
        var rnd = Random.Shared;
        var sb = new StringBuilder(512);
        sb.Append("""<svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">""");
        sb.Append("""<rect width="120" height="40" fill="#f0f2f5"/>""");
        for (var i = 0; i < 4; i++)   // 干扰线
            sb.Append($"""<line x1="{rnd.Next(120)}" y1="{rnd.Next(40)}" x2="{rnd.Next(120)}" y2="{rnd.Next(40)}" stroke="{RandColor(rnd)}" stroke-width="1"/>""");
        for (var i = 0; i < code.Length; i++)   // 字符
        {
            int x = 15 + i * 26, y = 28 + rnd.Next(-4, 4), rot = rnd.Next(-20, 20);
            sb.Append($"""<text x="{x}" y="{y}" font-size="26" font-family="monospace" fill="{RandColor(rnd)}" transform="rotate({rot} {x} {y})">{code[i]}</text>""");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RandColor(Random r) => $"#{r.Next(128):x2}{r.Next(128):x2}{r.Next(128):x2}";
}
