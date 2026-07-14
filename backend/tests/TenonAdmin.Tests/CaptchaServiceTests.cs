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
        // 配置桩不含键 → GetValueByKeyAsync 返回 null → 启用与否/类型均回退到 Options,保留这些用例原意
        return new CaptchaService([p], cache, new NullConfig(), sec);
    }

    // 只需 GetValueByKeyAsync 返回 null 的空配置桩;其余方法本测试不触及。
    private sealed class NullConfig : IConfigService
    {
        public Task<string?> GetValueByKeyAsync(string key) => Task.FromResult<string?>(null);
        public Task<PagedList<SysConfig>> PageAsync(ConfigPageInput input) => throw new NotImplementedException();
        public Task<SysConfig> GetAsync(long id) => throw new NotImplementedException();
        public Task<SiteInfoOutput> GetSiteInfoAsync() => throw new NotImplementedException();
        public Task SaveValuesAsync(IReadOnlyCollection<ConfigBatchItem> items) => throw new NotImplementedException();
        public Task<long> AddAsync(ConfigInput input) => throw new NotImplementedException();
        public Task UpdateAsync(long id, ConfigInput input) => throw new NotImplementedException();
        public Task DeleteAsync(long id) => throw new NotImplementedException();
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
        Assert.Equal("char", new SvgCaptchaProvider().Type);
        Assert.Equal(4, c.Code.Length);
        Assert.Contains("<svg", c.Svg);
        Assert.Contains("</svg>", c.Svg);
        Assert.All(c.Code, ch => Assert.Contains(ch.ToString(), c.Svg));
    }

    [Fact]
    public void Path_provider_never_leaks_plaintext_into_markup()
    {
        // path 生成器的招牌安全属性:字符渲染成 <polyline> 笔画,不产生任何 <text> 节点 → 抠不出可读字符。
        // (数字会出现在坐标/尺寸属性里,属噪声非答案;真正的保证是“无 text 元素”。)
        var p = new PathCaptchaProvider();
        Assert.Equal("path", p.Type);
        for (var n = 0; n < 200; n++)   // 覆盖随机字符集,顺带压覆盖度
        {
            var c = p.Generate();
            Assert.Equal(4, c.Code.Length);
            Assert.Contains("<polyline", c.Svg);
            Assert.DoesNotContain("<text", c.Svg);   // 明文绝不作为文本渲染
        }
    }

    [Fact]
    public void StrokeFont_covers_every_captcha_char()
    {
        // 去混淆字符集每个字符都要有“真字形”(非回退方框),否则 path 验证码会出方框
        foreach (var ch in "ABCDEFGHJKMNPQRSTUVWXYZ23456789")
            Assert.True(StrokeFont.HasGlyph(ch), $"missing glyph: {ch}");
    }

    [Fact]
    public void Math_provider_answer_is_numeric_and_not_shown()
    {
        var p = new MathCaptchaProvider();
        Assert.Equal("math", p.Type);
        for (var n = 0; n < 100; n++)
        {
            var c = p.Generate();
            Assert.True(int.TryParse(c.Code, out var v) && v >= 0);   // 明文是非负结果
            Assert.Contains("= ?", c.Svg);                            // 只露算式,不露结果
        }
    }

    [Fact]
    public async Task Issue_selects_provider_by_configured_type()
    {
        // 多生成器注册时,签发按 sys.security.captcha.type 选型(此处经 Options 回退,配置桩返回 null)
        var cache = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), new AdminCacheOptions());
        var sec = new AdminSecurityOptions { Captcha = new AdminCaptchaOptions { Enabled = true, Type = "path" } };
        var svc = new CaptchaService([new SvgCaptchaProvider(), new PathCaptchaProvider()], cache, new NullConfig(), sec);
        var c = await svc.IssueAsync();
        Assert.Equal("path", c.Type);
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
