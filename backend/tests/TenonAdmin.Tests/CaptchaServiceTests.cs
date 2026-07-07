using Microsoft.Extensions.Caching.Memory;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>验证码(§14)——由 scratchpad t8c-captcha-check 转正。签发/一次性消费/大小写/过期/关闭。</summary>
public class CaptchaServiceTests
{
    private static CaptchaService Make(ICaptchaProvider p, bool enabled)
    {
        var cache = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), new AdminCacheOptions());
        var sec = new AdminSecurityOptions { Captcha = new AdminCaptchaOptions { Enabled = enabled } };
        return new CaptchaService(p, cache, sec);
    }

    private static async Task<ErrorCode?> ErrOf(Func<Task> a)
    {
        try { await a(); return null; }
        catch (AdminException e) { return e.Code; }
    }

    [Fact]
    public void Svg_provider_generates_code_embedded_in_svg()
    {
        var c = new SvgCaptchaProvider().Generate();
        Assert.Equal(4, c.Code.Length);
        Assert.Contains("<svg", c.Svg);
        Assert.Contains("</svg>", c.Svg);
        Assert.All(c.Code, ch => Assert.Contains(ch.ToString(), c.Svg));
    }

    [Fact]
    public async Task Issue_then_validate_correct_passes_once()
    {
        var svc = Make(new StubProvider("aB3d"), enabled: true);
        var c = await svc.IssueAsync();
        Assert.False(string.IsNullOrEmpty(c.CaptchaId));

        Assert.Null(await ErrOf(() => svc.ValidateAsync(c.CaptchaId, "aB3d")));                       // 正确通过
        Assert.Equal(ErrorCode.CaptchaExpired, await ErrOf(() => svc.ValidateAsync(c.CaptchaId, "aB3d"))); // 一次性:已消费
    }

    [Fact]
    public async Task Validate_is_case_insensitive()
    {
        var svc = Make(new StubProvider("aB3d"), enabled: true);
        var c = await svc.IssueAsync();
        Assert.Null(await ErrOf(() => svc.ValidateAsync(c.CaptchaId, "AB3D")));
    }

    [Fact]
    public async Task Wrong_code_is_40003_and_consumes_ticket()
    {
        var svc = Make(new StubProvider("aB3d"), enabled: true);
        var c = await svc.IssueAsync();
        Assert.Equal(ErrorCode.CaptchaWrong, await ErrOf(() => svc.ValidateAsync(c.CaptchaId, "zzzz")));
        Assert.Equal(ErrorCode.CaptchaExpired, await ErrOf(() => svc.ValidateAsync(c.CaptchaId, "aB3d")));
    }

    [Fact]
    public async Task Missing_or_empty_is_40002()
    {
        var svc = Make(new StubProvider("aB3d"), enabled: true);
        Assert.Equal(ErrorCode.CaptchaExpired, await ErrOf(() => svc.ValidateAsync("nope", "aB3d")));
        Assert.Equal(ErrorCode.CaptchaExpired, await ErrOf(() => svc.ValidateAsync(null, null)));
    }

    [Fact]
    public async Task Disabled_passes_through()
    {
        var svc = Make(new StubProvider("aB3d"), enabled: false);
        Assert.Null(await ErrOf(() => svc.ValidateAsync(null, null)));
    }

    [Fact]
    public async Task Concurrent_validate_of_same_ticket_only_one_succeeds()
    {
        // P1-6:原子取删 → 并发复用同一 captchaId 时,只有一个通过,其余按已消费拒绝(杜绝单票放大猜测)
        var svc = Make(new StubProvider("aB3d"), enabled: true);
        var c = await svc.IssueAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => ErrOf(() => svc.ValidateAsync(c.CaptchaId, "aB3d"))));
        Assert.Equal(1, results.Count(r => r is null));   // 恰好一个成功
    }

    private sealed class StubProvider(string code) : ICaptchaProvider
    {
        public Captcha Generate() => new(code, "<svg/>");
    }
}
