using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 算术验证码(type=<c>math</c>)。渲染一道个位数四则题(如 <c>6 + 3 = ?</c>),明文=计算结果。
/// 换个「类型」而非「更强」:算式可被脚本直接求值,安全性约等于字符 SVG;胜在对真人更友好、抗简单撞库表单。
/// 要更抗自动化用 <c>path</c> 描边或前置替换重型生成器。
/// </summary>
public sealed class MathCaptchaProvider : ICaptchaProvider
{
    /// <inheritdoc />
    public string Type => "math";

    public Captcha Generate()
    {
        int a = RandomNumberGenerator.GetInt32(1, 10);
        int b = RandomNumberGenerator.GetInt32(1, 10);
        string sym;
        int answer;
        switch (RandomNumberGenerator.GetInt32(0, 3))
        {
            case 1:  // 减法:交换保证非负(a≥b),否则前端出现负号徒增困惑
                (a, b) = (Math.Max(a, b), Math.Min(a, b));
                (sym, answer) = ("-", a - b);
                break;
            case 2:
                (sym, answer) = ("×", a * b);
                break;
            default:
                (sym, answer) = ("+", a + b);
                break;
        }
        return new Captcha(answer.ToString(), Render($"{a} {sym} {b} = ?"));
    }

    private static string Render(string expr)
    {
        var rnd = Random.Shared;
        var sb = new StringBuilder(384);
        sb.Append("""<svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">""");
        sb.Append("""<rect width="120" height="40" fill="#f0f2f5"/>""");
        for (var i = 0; i < 3; i++)   // 干扰线
            sb.Append($"""<line x1="{rnd.Next(120)}" y1="{rnd.Next(40)}" x2="{rnd.Next(120)}" y2="{rnd.Next(40)}" stroke="{RandColor(rnd)}" stroke-width="1"/>""");
        // 明文=结果,不出现在算式里(算式只含运算数与「?」)
        sb.Append($"""<text x="10" y="28" font-size="22" font-family="monospace" fill="{RandColor(rnd)}">{expr}</text>""");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RandColor(Random r) => $"#{r.Next(120):x2}{r.Next(120):x2}{r.Next(120):x2}";
}
