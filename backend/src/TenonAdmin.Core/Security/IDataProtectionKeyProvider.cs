namespace TenonAdmin.Core;

/// <summary>
/// 数据保护密钥材料(版本 + 原始密钥字节)。供 <see cref="ISecretProtector"/> 做认证加密;
/// 版本号写入信封,支持后续密钥轮换与渐进解密。
/// </summary>
/// <param name="Version">密钥版本(正整数)</param>
/// <param name="Key">密钥字节(AES-256-GCM 建议 32 字节);调用方不得修改返回数组</param>
public sealed record DataProtectionKeyMaterial(int Version, byte[] Key);

/// <summary>
/// 数据保护密钥提供方扩展点(等保三级应用安全一期前置能力)。
/// 默认实现读 <c>TenonAdmin:Security:DataProtection</c>;消费方可前置注册对接 KMS/HSM。
/// </summary>
public interface IDataProtectionKeyProvider
{
    /// <summary>当前用于加密的密钥材料</summary>
    DataProtectionKeyMaterial GetCurrentKey();

    /// <summary>按版本取解密密钥;未知版本抛 <see cref="System.Security.Cryptography.CryptographicException"/></summary>
    DataProtectionKeyMaterial GetKey(int version);
}
