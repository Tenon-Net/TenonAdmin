using Microsoft.Extensions.Configuration;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOutboundCredentialProvider"/> 默认实现:<b>每次调用时</b>读 <c>TenonAdmin:Integration:Outbound:Targets:{name}:Secret</c>
/// (配置可来自环境变量、用户机密或任意支持热更新的配置提供方,轮换秘密无需重启);配置里没有时回落到注册模块时代码设置的值。
/// 前置注册同接口即可改为从密钥管理服务取。
/// </summary>
public class ConfigurationOutboundCredentialProvider(IntegrationOptions options, IConfiguration? configuration = null)
    : IOutboundCredentialProvider
{
    /// <inheritdoc />
    public virtual ValueTask<OutboundCredential?> GetAsync(OutboundTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var secret = configuration?[$"{IntegrationSetup.ConfigurationSection}:Outbound:Targets:{target.Name}:Secret"];
        if (string.IsNullOrEmpty(secret) && options.Outbound.Targets.TryGetValue(target.Name, out var configured))
            secret = configured.Secret;
        return ValueTask.FromResult(string.IsNullOrEmpty(secret) ? null : new OutboundCredential(secret, target.AuthUsername));
    }
}
