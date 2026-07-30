using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ITotpService"/> 默认实现:RFC 6238 TOTP over HMAC-SHA1,30s 步长,6 位,Base32 种子。
/// 纯 BCL,无第三方库;方法 virtual 便于覆写(如换步长/位数——不建议,会破坏 Authenticator 兼容)。
/// </summary>
public class TotpService(TimeProvider? time = null) : ITotpService
{
    /// <summary>时间步长(秒)</summary>
    public const int StepSeconds = 30;

    /// <summary>动态口令位数</summary>
    public const int Digits = 6;

    /// <summary>种子原始字节数(20 字节 = 160 位,RFC 4226 推荐)</summary>
    public const int SeedByteLength = 20;

    private static readonly char[] Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    /// <inheritdoc />
    public virtual string GenerateSeed()
    {
        var bytes = RandomNumberGenerator.GetBytes(SeedByteLength);
        return ToBase32(bytes);
    }

    /// <inheritdoc />
    public virtual string GetUri(string account, string issuer, string seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);

        // label = issuer:account,两侧都做 URI 编码;参数 secret 为 Base32 原样
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        var iss = Uri.EscapeDataString(issuer);
        var secret = NormalizeSeed(seed);
        return $"otpauth://totp/{label}?secret={secret}&issuer={iss}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    /// <inheritdoc />
    public virtual bool Verify(string seed, string code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(seed) || string.IsNullOrWhiteSpace(code)) return false;
        if (window < 0) window = 0;

        // 仅接受纯数字且长度匹配,防格式旁路
        code = code.Trim();
        if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return false;

        byte[] key;
        try { key = FromBase32(NormalizeSeed(seed)); }
        catch (FormatException) { return false; }

        if (key.Length == 0) return false;

        var timestep = CurrentTimeStep();
        // 恒定时间:扫完整窗口,不因提前匹配短路(防时序侧信道)
        var matched = false;
        for (var w = -window; w <= window; w++)
        {
            var expected = ComputeHotp(key, timestep + w);
            if (FixedTimeEqualsAscii(expected, code))
                matched = true;
        }
        return matched;
    }

    /// <inheritdoc />
    public virtual string ComputeCode(string seed, DateTimeOffset? utcNow = null)
    {
        var key = FromBase32(NormalizeSeed(seed));
        var step = utcNow is null
            ? CurrentTimeStep()
            : utcNow.Value.ToUnixTimeSeconds() / StepSeconds;
        return ComputeHotp(key, step);
    }

    /// <summary>当前 UTC 时间步(可由测试注入 <see cref="TimeProvider"/>)</summary>
    protected virtual long CurrentTimeStep()
    {
        var now = (time ?? TimeProvider.System).GetUtcNow();
        return now.ToUnixTimeSeconds() / StepSeconds;
    }

    /// <summary>HOTP(RFC 4226):HMAC-SHA1 → 动态截断 → 6 位十进制</summary>
    protected static string ComputeHotp(byte[] key, long counter)
    {
        // 计数器 8 字节大端
        Span<byte> counterBytes = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counterBytes, hash);

        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString($"D{Digits}", CultureInfo.InvariantCulture);
    }

    /// <summary>去空白、大写、剥填充</summary>
    protected static string NormalizeSeed(string seed) =>
        seed.Trim().Replace(" ", "", StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();

    /// <summary>RFC 4648 Base32 编码(无填充),输出大写。</summary>
    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return "";
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
            sb.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString();
    }

    /// <summary>RFC 4648 Base32 解码(容忍小写与填充)。</summary>
    public static byte[] FromBase32(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            throw new FormatException("空 Base32");

        encoded = NormalizeSeed(encoded);
        var output = new List<byte>(encoded.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var c in encoded)
        {
            var val = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= '2' and <= '7' => c - '2' + 26,
                _ => -1,
            };
            if (val < 0) throw new FormatException($"非法 Base32 字符: {c}");
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }
        return output.ToArray();
    }

    /// <summary>定长 ASCII 数字串恒定时间比较。</summary>
    private static bool FixedTimeEqualsAscii(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
