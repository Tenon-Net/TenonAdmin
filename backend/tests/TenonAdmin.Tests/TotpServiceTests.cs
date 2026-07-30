using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 原生 TOTP(RFC 6238):种子、URI、校验窗口、已知测试向量。驱动真实 <see cref="TotpService"/>。
/// </summary>
public class TotpServiceTests
{
    private readonly TotpService _totp = new();

    [Fact]
    public void GenerateSeed_is_base32_and_decodes_to_20_bytes()
    {
        var seed = _totp.GenerateSeed();
        Assert.False(string.IsNullOrWhiteSpace(seed));
        Assert.DoesNotContain('=', seed);
        var bytes = TotpService.FromBase32(seed);
        Assert.Equal(TotpService.SeedByteLength, bytes.Length);
    }

    [Fact]
    public void GetUri_contains_secret_and_issuer()
    {
        var seed = _totp.GenerateSeed();
        var uri = _totp.GetUri("admin", "TenonAdmin", seed);
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=" + seed, uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("issuer=TenonAdmin", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void Verify_accepts_current_code_and_rejects_wrong()
    {
        var seed = _totp.GenerateSeed();
        var code = _totp.ComputeCode(seed);
        Assert.Equal(6, code.Length);
        Assert.True(_totp.Verify(seed, code));
        Assert.False(_totp.Verify(seed, "000000"));
        Assert.False(_totp.Verify(seed, "abcdef"));
        Assert.False(_totp.Verify(seed, ""));
    }

    [Fact]
    public void Verify_window_accepts_adjacent_step()
    {
        var seed = _totp.GenerateSeed();
        var now = DateTimeOffset.UtcNow;
        // 前一步码在 window=1 时应通过
        var prev = _totp.ComputeCode(seed, now.AddSeconds(-30));
        Assert.True(_totp.Verify(seed, prev, window: 1));
        Assert.False(_totp.Verify(seed, prev, window: 0));
    }

    /// <summary>
    /// RFC 6238 Appendix B 测试向量(SHA-1, seed = "12345678901234567890", T=59 → 94287082 取 8 位;
    /// 我们实现 6 位,取模 10^6 → 287082)。
    /// </summary>
    [Fact]
    public void Rfc6238_sha1_vector_six_digits()
    {
        // 原始 20 字节 ASCII "12345678901234567890"
        var seedBytes = "12345678901234567890"u8.ToArray();
        var seed = TotpService.ToBase32(seedBytes);
        // T = 59 → counter = 1
        var at = DateTimeOffset.FromUnixTimeSeconds(59);
        var code = _totp.ComputeCode(seed, at);
        // HOTP 8 位参考 94287082 → 6 位 287082
        Assert.Equal("287082", code);
    }

    [Fact]
    public void Base32_roundtrip()
    {
        var original = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0xFF, 0xAB, 0xCD, 0xEF, 0x00 };
        var encoded = TotpService.ToBase32(original);
        var decoded = TotpService.FromBase32(encoded);
        Assert.Equal(original, decoded);
    }
}
