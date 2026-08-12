using System.Globalization;
using System.Text.RegularExpressions;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IAvatarUrlValidator"/> 默认实现。只认 <see cref="IFileUrlSigner.BuildUrl"/> 产出的形状——
/// 相对路径、同源、带合法签名;协议前缀(<c>http(s)://</c>)、多余查询参数、路径大小写偏差、伪造签名都直接拒绝(纵深防御)。
/// </summary>
public partial class AvatarUrlValidator(IFileUrlSigner signer) : IAvatarUrlValidator
{
    // 与 FileUrlSigner.BuildUrl 的形状严格对齐:/api/v1/sys/file/{数字 id}/view?sig={base64url}
    [GeneratedRegex(@"^/api/v1/sys/file/(\d+)/view\?sig=([A-Za-z0-9_-]+)$")]
    private static partial Regex LocalSignedUrlPattern();

    /// <inheritdoc />
    public virtual bool IsValid(string? avatar)
    {
        if (string.IsNullOrWhiteSpace(avatar)) return true;

        var m = LocalSignedUrlPattern().Match(avatar);
        if (!m.Success) return false;   // 非本地签名直链形状(含外部 http(s)://、同源但缺签名等)一律拒绝

        var fileId = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return signer.Verify(fileId, m.Groups[2].Value);
    }
}
