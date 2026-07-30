using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISecretProtector"/> 默认实现:AES-256-GCM 认证加密(BCL,零第三方依赖)。
/// 信封格式 <c>{version}:{nonceB64}:{ciphertextB64}:{tagB64}</c>(标准 Base64)。
/// </summary>
public class AesGcmSecretProtector(IDataProtectionKeyProvider keys) : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <inheritdoc />
    public virtual string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var material = keys.GetCurrentKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(material.Key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        return string.Join(':',
            material.Version.ToString(),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(cipher),
            Convert.ToBase64String(tag));
    }

    /// <inheritdoc />
    public virtual string Unprotect(string envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope))
            throw new CryptographicException("空的秘密保护信封");

        var parts = envelope.Split(':');
        if (parts.Length != 4)
            throw new CryptographicException("秘密保护信封格式无效");

        if (!int.TryParse(parts[0], out var version) || version <= 0)
            throw new CryptographicException("秘密保护信封版本无效");

        byte[] nonce, cipher, tag;
        try
        {
            nonce = Convert.FromBase64String(parts[1]);
            cipher = Convert.FromBase64String(parts[2]);
            tag = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("秘密保护信封 Base64 无效", ex);
        }

        if (nonce.Length != NonceSize || tag.Length != TagSize)
            throw new CryptographicException("秘密保护信封 nonce/tag 长度无效");

        var material = keys.GetKey(version);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(material.Key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
