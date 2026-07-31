using System.Security.Cryptography;
using System.Text;

namespace TenonAdmin.Services;

/// <summary>
/// 高熵一次性凭据(邀请 token / 恢复码 / 部署授权)的哈希工具。
/// 输入已具足够熵,用 SHA-256 即可;不做 PBKDF2(避免无意义延迟)。
/// 比较走恒定时间。
/// </summary>
internal static class SecretHash
{
    /// <summary>SHA-256 → 小写 hex(64 字符)。</summary>
    public static string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>恒定时间比较哈希(或任意等长 hex/字符串)。</summary>
    public static bool FixedEquals(string a, string b)
    {
        if (a is null || b is null || a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }
}
