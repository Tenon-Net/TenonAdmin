using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// MFA 强制策略:判定用户是否必须启用 TOTP,以及有效高敏权限集合。
/// </summary>
public interface IMfaPolicyService
{
    /// <summary>
    /// TOTP 能力是否开启:历史 Level3 ∨ <c>Totp:Enabled</c>(Options) ∨ SysConfig <c>sys.security.totp.enabled</c>。
    /// 与登录验证码同款——配置中心改值即时生效。
    /// </summary>
    Task<bool> IsTotpFeatureEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 用户是否被强制 MFA:能力开启前提下,账号 ForceTotp / 超管必绑 / (过渡)高敏权限。
    /// 显式 ForceTotp 只能加严,不能覆盖自动强制。
    /// </summary>
    Task<bool> IsMfaRequiredAsync(SysUser user, CancellationToken cancellationToken = default);

    /// <summary>有效高敏权限集合 = 内核默认 ∪ 消费者自定义追加(库表)。默认项不可移除。</summary>
    Task<IReadOnlySet<string>> GetEffectiveHighSensitivityPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>用户当前权限码是否与有效高敏集合相交。</summary>
    Task<bool> HoldsHighSensitivityPermissionAsync(long userId, CancellationToken cancellationToken = default);
}
