using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOpenAppKeyGenerator"/> 默认实现:<see cref="RandomNumberGenerator"/> 产生 8 字节公开标识与 32 字节秘密,
/// 摘要取 SHA-256。秘密是 256 位高熵随机数,离线穷举不可行,不需要 PBKDF2 一类慢哈希(那是给人类口令的)。
/// </summary>
public class OpenAppKeyGenerator : IOpenAppKeyGenerator
{
    /// <inheritdoc />
    public virtual OpenAppGeneratedKey Generate()
    {
        var keyId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(OpenAppApiKey.KeyIdLength / 2));
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return new OpenAppGeneratedKey(keyId, secret, OpenAppApiKey.Format(keyId, secret), ComputeSecretHash(secret));
    }

    /// <inheritdoc />
    public virtual string ComputeSecretHash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }
}
