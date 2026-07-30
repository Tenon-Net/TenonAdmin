using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// MFA 强制策略:判定用户是否必须启用 TOTP,以及有效高敏权限集合。
/// </summary>
public interface IMfaPolicyService
{
    /// <summary>
    /// 用户是否被强制 MFA:超级管理员 OR <see cref="SysUser.ForceTotp"/> OR 持有任一高敏权限。
    /// 显式 ForceTotp 只能加严,不能覆盖自动强制。
    /// </summary>
    Task<bool> IsMfaRequiredAsync(SysUser user, CancellationToken cancellationToken = default);

    /// <summary>有效高敏权限集合 = 内核默认 ∪ 消费者自定义追加(库表)。默认项不可移除。</summary>
    Task<IReadOnlySet<string>> GetEffectiveHighSensitivityPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>用户当前权限码是否与有效高敏集合相交。</summary>
    Task<bool> HoldsHighSensitivityPermissionAsync(long userId, CancellationToken cancellationToken = default);
}
