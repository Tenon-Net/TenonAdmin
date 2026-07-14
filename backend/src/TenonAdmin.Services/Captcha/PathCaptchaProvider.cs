using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 描边字形验证码(type=<c>path</c>)。每个字符渲染成 SVG <c>&lt;polyline&gt;</c> 笔画,
/// <b>明文绝不作为文本出现在标记里</b>——直接堵死 <c>SvgCaptchaProvider</c> 的已知天花板
/// (从 <c>&lt;text&gt;</c> 节点直接抠字符)。要真正抠字得先渲染再 OCR,门槛显著抬高。仍零绘图依赖、跨平台。
/// </summary>
// ponytail: 自带极简描边字库(StrokeFont),不引任何字体/绘图依赖。够登录验证码用;
//           要更抗自动识别(扭曲/滑块/行为)按 ICaptchaProvider 前置替换,内核不背这个重量。
public sealed class PathCaptchaProvider : ICaptchaProvider
{
    private const string CHARSET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LENGTH = 4;

    /// <inheritdoc />
    public string Type => "path";

    public Captcha Generate()
    {
        // 明文:加密安全随机(验证码强度相关,不用 Random)
        var code = string.Create(LENGTH, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = CHARSET[RandomNumberGenerator.GetInt32(CHARSET.Length)];
        });
        return new Captcha(code, Render(code));
    }

    /// <summary>把验证码渲染成描边 SVG。视觉抖动(位置/颜色/旋转)非安全项,用普通 <see cref="Random"/> 即可。</summary>
    private static string Render(string code)
    {
        var rnd = Random.Shared;
        var sb = new StringBuilder(1024);
        sb.Append("""<svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">""");
        sb.Append("""<rect width="120" height="40" fill="#f0f2f5"/>""");
        for (var i = 0; i < 5; i++)   // 干扰线
            sb.Append($"""<line x1="{rnd.Next(120)}" y1="{rnd.Next(40)}" x2="{rnd.Next(120)}" y2="{rnd.Next(40)}" stroke="{RandColor(rnd)}" stroke-width="1"/>""");
        for (var i = 0; i < code.Length; i++)
        {
            double ox = 12 + i * 26 + rnd.Next(-2, 3);   // 字格左上;网格 6×10 → ×3 得 18×30
            double oy = 4 + rnd.Next(-2, 3);
            int rot = rnd.Next(-18, 18);
            double cx = ox + 9, cy = oy + 15;            // 旋转中心 = 字格中点
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                """<g transform="rotate({0} {1:0.#} {2:0.#})" stroke="{3}" stroke-width="2" fill="none" stroke-linejoin="round" stroke-linecap="round">""",
                rot, cx, cy, RandColor(rnd)));
            foreach (var poly in StrokeFont.Glyph(code[i]))
            {
                sb.Append("<polyline points=\"");
                for (var p = 0; p < poly.Length; p += 2)
                {
                    if (p > 0) sb.Append(' ');
                    var x = ox + poly[p] * 3.0;
                    var y = oy + poly[p + 1] * 3.0;
                    sb.Append(x.ToString("0.#", CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.#", CultureInfo.InvariantCulture));
                }
                sb.Append("\"/>");
            }
            sb.Append("</g>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RandColor(Random r) => $"#{r.Next(120):x2}{r.Next(120):x2}{r.Next(120):x2}";
}

/// <summary>
/// 极简描边字库:字符 → 笔画折线集(网格 6 宽 × 10 高,y 向下)。每条折线是扁平点数组 [x0,y0,x1,y1,…]。
/// 仅覆盖验证码去混淆字符集(A-Z 去 ILO + 2-9)。缺字回退方框,永不抛。
/// </summary>
public static class StrokeFont
{
    /// <summary>取字形笔画;未定义则回退一个方框(保证 <see cref="PathCaptchaProvider"/> 永不抛)。</summary>
    public static int[][] Glyph(char c) => Glyphs.TryGetValue(char.ToUpperInvariant(c), out var g) ? g : Box;

    /// <summary>是否为“真字形”(非回退方框)——供覆盖度自检。</summary>
    public static bool HasGlyph(char c) => Glyphs.ContainsKey(char.ToUpperInvariant(c));

    private static readonly int[][] Box = [[0, 0, 6, 0, 6, 10, 0, 10, 0, 0]];

    private static readonly Dictionary<char, int[][]> Glyphs = new()
    {
        ['A'] = [[0, 10, 3, 0, 6, 10], [1, 6, 5, 6]],
        ['B'] = [[0, 0, 0, 10], [0, 0, 4, 0, 5, 1, 5, 4, 4, 5, 0, 5], [0, 5, 5, 5, 6, 6, 6, 9, 5, 10, 0, 10]],
        ['C'] = [[6, 2, 4, 0, 2, 0, 0, 2, 0, 8, 2, 10, 4, 10, 6, 8]],
        ['D'] = [[0, 0, 0, 10], [0, 0, 4, 0, 6, 2, 6, 8, 4, 10, 0, 10]],
        ['E'] = [[6, 0, 0, 0, 0, 10, 6, 10], [0, 5, 4, 5]],
        ['F'] = [[6, 0, 0, 0, 0, 10], [0, 5, 4, 5]],
        ['G'] = [[6, 2, 4, 0, 2, 0, 0, 2, 0, 8, 2, 10, 4, 10, 6, 8, 6, 5, 4, 5]],
        ['H'] = [[0, 0, 0, 10], [6, 0, 6, 10], [0, 5, 6, 5]],
        ['J'] = [[6, 0, 6, 8, 4, 10, 2, 10, 0, 8]],
        ['K'] = [[0, 0, 0, 10], [6, 0, 0, 5, 6, 10]],
        ['M'] = [[0, 10, 0, 0, 3, 5, 6, 0, 6, 10]],
        ['N'] = [[0, 10, 0, 0, 6, 10, 6, 0]],
        ['P'] = [[0, 10, 0, 0, 4, 0, 5, 1, 5, 4, 4, 5, 0, 5]],
        ['Q'] = [[3, 0, 1, 1, 0, 3, 0, 7, 1, 9, 3, 10, 5, 9, 6, 7, 6, 3, 5, 1, 3, 0], [4, 7, 6, 10]],
        ['R'] = [[0, 10, 0, 0, 4, 0, 5, 1, 5, 4, 4, 5, 0, 5], [3, 5, 6, 10]],
        ['S'] = [[6, 2, 4, 0, 2, 0, 0, 2, 2, 5, 4, 5, 6, 8, 4, 10, 2, 10, 0, 8]],
        ['T'] = [[0, 0, 6, 0], [3, 0, 3, 10]],
        ['U'] = [[0, 0, 0, 8, 2, 10, 4, 10, 6, 8, 6, 0]],
        ['V'] = [[0, 0, 3, 10, 6, 0]],
        ['W'] = [[0, 0, 1, 10, 3, 4, 5, 10, 6, 0]],
        ['X'] = [[0, 0, 6, 10], [6, 0, 0, 10]],
        ['Y'] = [[0, 0, 3, 5, 6, 0], [3, 5, 3, 10]],
        ['Z'] = [[0, 0, 6, 0, 0, 10, 6, 10]],
        ['2'] = [[0, 2, 2, 0, 4, 0, 6, 2, 6, 4, 0, 10, 6, 10]],
        ['3'] = [[0, 1, 3, 0, 5, 1, 5, 4, 3, 5, 5, 6, 5, 9, 3, 10, 0, 9]],
        ['4'] = [[4, 10, 4, 0, 0, 6, 6, 6]],
        ['5'] = [[6, 0, 1, 0, 1, 4, 4, 4, 6, 6, 6, 8, 4, 10, 1, 10, 0, 8]],
        ['6'] = [[5, 1, 3, 0, 1, 2, 0, 5, 0, 8, 2, 10, 4, 10, 6, 8, 6, 6, 4, 5, 1, 5]],
        ['7'] = [[0, 0, 6, 0, 2, 10]],
        ['8'] = [[3, 5, 1, 4, 1, 1, 3, 0, 5, 1, 5, 4, 3, 5, 1, 6, 1, 9, 3, 10, 5, 9, 5, 6, 3, 5]],
        ['9'] = [[1, 9, 3, 10, 5, 10, 6, 7, 6, 2, 4, 0, 2, 0, 0, 2, 0, 4, 2, 5, 5, 5]],
    };
}
