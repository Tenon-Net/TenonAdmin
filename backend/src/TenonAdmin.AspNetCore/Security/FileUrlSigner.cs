using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// <see cref="IFileUrlSigner"/> 的 HMAC-SHA256 默认实现。
/// <para>签名密钥<b>不直接用签令牌的那把</b>,而是由它派生出一把子密钥
/// (<c>HMAC(jwtKey, "tenon:file-view")</c>)——两个用途各用各的密钥材料是密码学卫生:
/// 万一某处签名实现出岔子,也不会波及令牌签发。</para>
/// <para>没有额外的密钥配置项:直链的生命周期天然跟着 JWT 密钥走,轮换密钥即吊销全部直链(见接口注释)。</para>
/// </summary>
public sealed class FileUrlSigner(SymmetricSecurityKey jwtKey) : IFileUrlSigner
{
    /// <summary>派生用途标签:同一把主密钥下,不同用途走不同子密钥。</summary>
    private const string PURPOSE = "tenon:file-view";

    private readonly byte[] _subKey = HMACSHA256.HashData(jwtKey.Key, Encoding.UTF8.GetBytes(PURPOSE));

    /// <inheritdoc />
    public string Sign(long fileId) =>
        Base64UrlEncoder.Encode(HMACSHA256.HashData(_subKey, Encoding.UTF8.GetBytes(fileId.ToString(CultureInfo.InvariantCulture))));

    /// <inheritdoc />
    public bool Verify(long fileId, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        byte[] provided;
        try { provided = Base64UrlEncoder.DecodeBytes(signature); }
        catch { return false; }   // 畸形签名(非 base64url)= 校验失败,不是 500

        var expected = HMACSHA256.HashData(_subKey, Encoding.UTF8.GetBytes(fileId.ToString(CultureInfo.InvariantCulture)));
        return CryptographicOperations.FixedTimeEquals(expected, provided);   // 定长比较:不给按前缀爆破留时间侧信道
    }

    /// <inheritdoc />
    public string BuildUrl(long fileId) =>
        $"/api/v1/sys/file/{fileId}/view?sig={Sign(fileId)}";
}
