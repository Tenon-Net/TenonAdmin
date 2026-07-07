// T8c 自检:验证码 —— SVG 生成 + CaptchaService 签发/一次性消费/大小写/过期/关闭直通。
// 运行:dotnet run t8c-captcha-check.cs
#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property PublishAot=false

using Microsoft.Extensions.Caching.Memory;
using TenonAdmin.Core;
using TenonAdmin.Services;

int passed = 0, total = 0;
void Check(string name, bool ok)
{
    total++;
    if (ok) { passed++; Console.WriteLine($"  ✓ {name}"); }
    else Console.WriteLine($"  ✗ {name}  <<< 失败");
}
static ErrorCode? ErrOf(Func<Task> a)
{
    try { a().GetAwaiter().GetResult(); return null; }
    catch (AdminException e) { return e.Code; }
}

// 1) 真 SVG 生成器
var svg = new SvgCaptchaProvider().Generate();
Check("SVG 码长 4", svg.Code.Length == 4);
Check("SVG 含 <svg>/</svg>", svg.Svg.Contains("<svg") && svg.Svg.Contains("</svg>"));
Check("SVG 内嵌了验证码字符", svg.Code.All(c => svg.Svg.Contains(c)));

// 固定码桩,便于测校验逻辑
var stub = new StubProvider("aB3d");
static CaptchaService Make(ICaptchaProvider p, bool enabled)
{
    var cache = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), new AdminCacheOptions());
    var sec = new AdminSecurityOptions { Captcha = new AdminCaptchaOptions { Enabled = enabled } };
    return new CaptchaService(p, cache, sec);
}

// 2) 启用:签发 + 正确校验通过
var svc = Make(stub, enabled: true);
var c1 = await svc.IssueAsync();
Check("签发返回票据 Id + SVG", !string.IsNullOrEmpty(c1.CaptchaId) && c1.Svg == "<svg/>");
Check("正确码校验通过", ErrOf(() => svc.ValidateAsync(c1.CaptchaId, "aB3d")) is null);
Check("一次性:同票据再校验即过期(40002)", ErrOf(() => svc.ValidateAsync(c1.CaptchaId, "aB3d")) == ErrorCode.CaptchaExpired);

// 3) 大小写不敏感
var c2 = await svc.IssueAsync();
Check("大小写不敏感通过", ErrOf(() => svc.ValidateAsync(c2.CaptchaId, "AB3D")) is null);

// 4) 错误码 40003,且错误尝试也消费掉票据
var c3 = await svc.IssueAsync();
Check("错误码 → 40003", ErrOf(() => svc.ValidateAsync(c3.CaptchaId, "zzzz")) == ErrorCode.CaptchaWrong);
Check("错误尝试也消费票据(再试 40002)", ErrOf(() => svc.ValidateAsync(c3.CaptchaId, "aB3d")) == ErrorCode.CaptchaExpired);

// 5) 缺失/空 → 40002
Check("不存在票据 → 40002", ErrOf(() => svc.ValidateAsync("nope", "aB3d")) == ErrorCode.CaptchaExpired);
Check("空票据/空码 → 40002", ErrOf(() => svc.ValidateAsync(null, null)) == ErrorCode.CaptchaExpired);

// 6) 关闭:直通(不校验)
var off = Make(stub, enabled: false);
Check("关闭时校验直通", ErrOf(() => off.ValidateAsync(null, null)) is null);

Console.WriteLine($"\n结果:{passed}/{total} 通过");
if (passed != total) Environment.Exit(1);

sealed class StubProvider(string code) : ICaptchaProvider
{
    public Captcha Generate() => new(code, "<svg/>");
}
