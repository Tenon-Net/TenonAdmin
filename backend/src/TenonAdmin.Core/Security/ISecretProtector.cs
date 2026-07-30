namespace TenonAdmin.Core;

/// <summary>
/// 窄域秘密保护扩展点:把明文(如 TOTP seed、HMAC ClientSecret)封成可存库的信封串。
/// <para>信封格式由实现定义;默认 AES-GCM 为 <c>version:nonce:ciphertext:tag</c>(各段 Base64Url 或 Base64)。
/// 不可用于需要精确查询的业务字段——那是第三期 <c>IFieldProtector</c> 的范畴。</para>
/// 注册为 Singleton;<c>TryAdd</c> 可整体替换。
/// </summary>
public interface ISecretProtector
{
    /// <summary>加密明文 → 信封字符串(可安全落库)</summary>
    string Protect(string plaintext);

    /// <summary>解密信封 → 明文;密钥错误/篡改/畸形抛加密异常</summary>
    string Unprotect(string envelope);
}
