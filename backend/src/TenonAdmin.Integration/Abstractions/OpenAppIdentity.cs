using System.Globalization;
using System.Security.Claims;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>接入应用主体的 claim 名常量(禁硬编码字符串纪律,与内核 <see cref="TokenClaimNames"/> 并列)。</summary>
public static class OpenAppClaimTypes
{
    /// <summary>接入应用 Id</summary>
    public const string AppId = "tenon_app_id";

    /// <summary>接入应用编码(同时作 <see cref="ClaimsIdentity.Name"/>)</summary>
    public const string AppCode = "tenon_app_code";

    /// <summary>本次使用的凭据 Id</summary>
    public const string CredentialId = "tenon_app_credential";

    /// <summary>本次使用的凭据公开标识(非秘密)</summary>
    public const string KeyId = "tenon_app_key";

    /// <summary>应用状态版本(授权/范围缓存键的一部分)</summary>
    public const string StateVersion = "tenon_app_version";
}

/// <summary>
/// 系统身份载体:一次校验通过的接入应用身份(实现契约 §4.2)。
/// <para>转成 <see cref="ClaimsPrincipal"/> 时<b>刻意不写</b> <c>sub</c>/<c>sid</c>/<c>sadm</c>:应用不是登录用户,
/// <see cref="ICurrentUser.UserId"/> 为空,审计 AOP 不会伪造 <c>CreateUserId</c>;也进不了只认用户会话的
/// <c>[RolePermission]</c>/<c>[ActiveSession]</c> 通道。设置了归属机构时写内核 <see cref="TokenClaimNames.ORG_ID"/>,
/// 未显式赋值的插入行据此锚定 <c>CreateOrgId</c>。</para>
/// </summary>
/// <param name="AppId">接入应用 Id</param>
/// <param name="AppCode">接入应用编码</param>
/// <param name="CredentialId">本次使用的凭据 Id</param>
/// <param name="KeyId">凭据公开标识(非秘密)</param>
/// <param name="OwnerOrgId">应用归属机构(数据范围锚点);未设置为 null</param>
/// <param name="StateVersion">应用状态版本</param>
public sealed record OpenAppIdentity(
    long AppId,
    string AppCode,
    long CredentialId,
    string KeyId,
    long? OwnerOrgId,
    long StateVersion)
{
    /// <summary>构造认证主体;<paramref name="authenticationType"/> 即认证方案名。</summary>
    public ClaimsPrincipal ToPrincipal(string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        var claims = new List<Claim>
        {
            new(OpenAppClaimTypes.AppId, AppId.ToString(CultureInfo.InvariantCulture)),
            new(OpenAppClaimTypes.AppCode, AppCode),
            new(OpenAppClaimTypes.CredentialId, CredentialId.ToString(CultureInfo.InvariantCulture)),
            new(OpenAppClaimTypes.KeyId, KeyId),
            new(OpenAppClaimTypes.StateVersion, StateVersion.ToString(CultureInfo.InvariantCulture)),
        };
        if (OwnerOrgId is { } org)
            claims.Add(new Claim(TokenClaimNames.ORG_ID, org.ToString(CultureInfo.InvariantCulture)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType, OpenAppClaimTypes.AppCode, roleType: null));
    }

    /// <summary>从主体还原;不是接入应用主体(缺少或无法解析必需 claim)时返回 null。</summary>
    public static OpenAppIdentity? FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        if (!TryLong(principal, OpenAppClaimTypes.AppId, out var appId)
            || !TryLong(principal, OpenAppClaimTypes.CredentialId, out var credentialId)
            || !TryLong(principal, OpenAppClaimTypes.StateVersion, out var version))
            return null;

        var code = principal.FindFirstValue(OpenAppClaimTypes.AppCode);
        var keyId = principal.FindFirstValue(OpenAppClaimTypes.KeyId);
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(keyId)) return null;

        long? org = TryLong(principal, TokenClaimNames.ORG_ID, out var o) ? o : null;
        return new OpenAppIdentity(appId, code, credentialId, keyId, org, version);
    }

    private static bool TryLong(ClaimsPrincipal principal, string type, out long value) =>
        long.TryParse(principal.FindFirstValue(type), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
