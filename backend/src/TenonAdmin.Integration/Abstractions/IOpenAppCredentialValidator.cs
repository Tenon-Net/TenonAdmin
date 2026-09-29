namespace TenonAdmin.Integration;

/// <summary>凭据无效的具体原因。只用于调用记录与排障,对外一律返回 <see cref="IntegrationErrorCode.CredentialInvalid"/>。</summary>
public enum OpenAppCredentialFailure
{
    /// <summary>校验通过</summary>
    None = 0,

    /// <summary>未提供凭据</summary>
    Missing = 1,

    /// <summary>格式不合法(未查库)</summary>
    Malformed = 2,

    /// <summary>凭据标识不存在</summary>
    NotFound = 3,

    /// <summary>秘密与摘要不符</summary>
    SecretMismatch = 4,

    /// <summary>凭据已撤销</summary>
    Revoked = 5,

    /// <summary>凭据已过期</summary>
    Expired = 6,

    /// <summary>接入应用已停用</summary>
    AppDisabled = 7,

    /// <summary>接入应用不存在或已删除</summary>
    AppNotFound = 8,
}

/// <summary>
/// 一次凭据校验的结果。失败时 <see cref="Identity"/> 为 null;凭据标识可识别时带上 <see cref="AppId"/>/<see cref="CredentialId"/>/<see cref="KeyId"/>,
/// 供调用记录定位是哪个应用在失败——<b>永不携带秘密或原始凭据</b>。
/// </summary>
/// <param name="Failure">失败原因;<see cref="OpenAppCredentialFailure.None"/> 表示通过</param>
/// <param name="Identity">通过时的系统身份载体</param>
/// <param name="AppId">可识别时的应用 Id</param>
/// <param name="CredentialId">可识别时的凭据 Id</param>
/// <param name="KeyId">格式合法时的凭据公开标识</param>
public sealed record OpenAppCredentialValidation(
    OpenAppCredentialFailure Failure,
    OpenAppIdentity? Identity,
    long? AppId = null,
    long? CredentialId = null,
    string? KeyId = null)
{
    /// <summary>是否通过</summary>
    public bool IsValid => Failure == OpenAppCredentialFailure.None && Identity is not null;

    /// <summary>通过</summary>
    public static OpenAppCredentialValidation Success(OpenAppIdentity identity) =>
        new(OpenAppCredentialFailure.None, identity, identity.AppId, identity.CredentialId, identity.KeyId);

    /// <summary>失败(不携带身份)</summary>
    public static OpenAppCredentialValidation Fail(
        OpenAppCredentialFailure failure,
        long? appId = null,
        long? credentialId = null,
        string? keyId = null) =>
        new(failure, null, appId, credentialId, keyId);
}

/// <summary>
/// 接入凭据校验服务(实现契约 §4.1)。默认实现 <see cref="OpenAppCredentialValidator"/>:按唯一凭据标识取库后比较摘要,
/// 依次判定撤销、过期与应用状态;每次读库,因此停用/撤销在所有副本上下一次请求即生效。
/// <para>消费者在 <c>AddTenonAdminIntegration</c> 之前注册同接口即可整体替换(例如接企业身份平台)。</para>
/// </summary>
public interface IOpenAppCredentialValidator
{
    /// <summary>校验调用方出示的凭据原文;实现不得记录、回显或在异常里携带该原文。</summary>
    Task<OpenAppCredentialValidation> ValidateAsync(string? presentedKey, CancellationToken cancellationToken = default);
}
