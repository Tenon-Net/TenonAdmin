using System.Net.Http.Headers;
using System.Text;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOutboundAuthenticator"/> 默认实现:按目标 <c>Auth:Type</c> 施加凭据(无 / Bearer / 自定义头 / Basic)。
/// 秘密每次从 <see cref="IOutboundCredentialProvider"/> 取得;缺失或含请求头不允许的字符时抛
/// <see cref="OutboundCredentialUnavailableException"/>,请求不会发出。签名、换取令牌等协议由适配器覆写认证步骤实现。
/// </summary>
public class ConfiguredOutboundAuthenticator(IOutboundCredentialProvider credentials) : IOutboundAuthenticator
{
    /// <inheritdoc />
    public virtual async ValueTask ApplyAsync(HttpRequestMessage request, OutboundTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        if (target.AuthType == OutboundAuthType.None) return;

        var credential = await credentials.GetAsync(target, cancellationToken);
        if (credential is null || string.IsNullOrEmpty(credential.Secret))
            throw new OutboundCredentialUnavailableException(target.Name, OutboundCredentialUnavailableException.MissingReason);

        switch (target.AuthType)
        {
            case OutboundAuthType.Bearer:
                EnsureHeaderSafe(target, credential.Secret);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret);
                break;
            case OutboundAuthType.Header:
                EnsureHeaderSafe(target, credential.Secret);
                request.Headers.Remove(target.AuthHeaderName!);
                request.Headers.TryAddWithoutValidation(target.AuthHeaderName!, credential.Secret);
                break;
            case OutboundAuthType.Basic:
                var username = credential.Username ?? target.AuthUsername ?? "";
                EnsureHeaderSafe(target, username);
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{credential.Secret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
                break;
        }
    }

    private static void EnsureHeaderSafe(OutboundTarget target, string value)
    {
        if (!OutboundHeaders.IsValidValue(value))
            throw new OutboundCredentialUnavailableException(target.Name, OutboundCredentialUnavailableException.InvalidReason);
    }
}
