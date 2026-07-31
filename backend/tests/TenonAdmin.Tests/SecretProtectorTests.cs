using System.Security.Cryptography;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 数据保护密钥 + AES-GCM 秘密保护:往返加解密、错密钥失败。
/// 驱动真实 <see cref="LocalDataProtectionKeyProvider"/> / <see cref="AesGcmSecretProtector"/>。
/// </summary>
public class SecretProtectorTests
{
    private static (IDataProtectionKeyProvider keys, ISecretProtector protector) Make(
        string? keyB64 = null,
        int version = 1,
        SecurityProfile profile = SecurityProfile.None)
    {
        keyB64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var security = new AdminSecurityOptions
        {
            Profile = profile,
            DataProtection = new AdminDataProtectionOptions { Key = keyB64, KeyVersion = version },
        };
        var keys = new LocalDataProtectionKeyProvider(security, contentRoot: null, isDevelopment: true);
        return (keys, new AesGcmSecretProtector(keys));
    }

    [Fact]
    public void Protect_unprotect_round_trips()
    {
        var (_, protector) = Make();
        const string plain = "JBSWY3DPEHPK3PXP"; // 典型 TOTP seed 形态
        var envelope = protector.Protect(plain);
        Assert.False(string.IsNullOrWhiteSpace(envelope));
        Assert.DoesNotContain(plain, envelope);
        Assert.Equal(plain, protector.Unprotect(envelope));
    }

    [Fact]
    public void Envelope_contains_version_and_parts()
    {
        var (_, protector) = Make(version: 3);
        var envelope = protector.Protect("secret-value");
        var parts = envelope.Split(':');
        Assert.Equal(4, parts.Length);
        Assert.Equal("3", parts[0]);
    }

    [Fact]
    public void Wrong_key_fails_to_unprotect()
    {
        var keyA = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyB = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var (_, pA) = Make(keyA);
        var (_, pB) = Make(keyB);

        var envelope = pA.Protect("totp-seed-material");
        Assert.ThrowsAny<CryptographicException>(() => pB.Unprotect(envelope));
    }

    [Fact]
    public void Tampered_ciphertext_fails()
    {
        var (_, protector) = Make();
        var envelope = protector.Protect("hello");
        var parts = envelope.Split(':');
        // 篡改密文段
        var cipher = Convert.FromBase64String(parts[2]);
        cipher[0] ^= 0xFF;
        parts[2] = Convert.ToBase64String(cipher);
        var bad = string.Join(':', parts);
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(bad));
    }

    [Fact]
    public void Level3_without_configured_key_throws()
    {
        var security = new AdminSecurityOptions
        {
            Profile = SecurityProfile.Level3,
            DataProtection = new AdminDataProtectionOptions { Key = null },
        };
        Assert.Throws<InvalidOperationException>(() =>
            new LocalDataProtectionKeyProvider(security, contentRoot: Path.GetTempPath(), isDevelopment: true));
    }

    [Fact]
    public void Configured_key_is_used_as_current_version()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var (keys, _) = Make(key, version: 7);
        var material = keys.GetCurrentKey();
        Assert.Equal(7, material.Version);
        Assert.Equal(Convert.FromBase64String(key), material.Key);
        Assert.Equal(material.Key, keys.GetKey(7).Key);
        Assert.ThrowsAny<CryptographicException>(() => keys.GetKey(99));
    }
}
