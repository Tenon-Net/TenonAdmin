using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IMfaPolicyService"/> 默认实现。默认高敏集合见 <see cref="HighSensitivityPermissions.Default"/>。
/// <para>强制 MFA 仅在 <see cref="SecurityProfile.Level3"/> 下生效——非 Level3 保持密码直通兼容(ADR 0005 / 一期兼容冻结)。</para>
/// </summary>
public class MfaPolicyService(
    IPermissionProvider permissions,
    IRepository<SysHighSensitivityPermission> customHighSens,
    ISecurityProfileAccessor profile,
    // 超管在权限码集合中通常为空(授权管道旁路);IsSuperAdmin 字段直接判定
    IRepository<SysUser>? users = null) : IMfaPolicyService
{
    /// <inheritdoc />
    public virtual async Task<bool> IsMfaRequiredAsync(SysUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        // 非 Level3:不强制 TOTP(既有项目升级零阻断;短信 MFA 仍由独立开关控制)
        if (!profile.IsLevel3) return false;
        if (user.IsSuperAdmin) return true;
        if (user.ForceTotp) return true;
        return await HoldsHighSensitivityPermissionAsync(user.Id, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlySet<string>> GetEffectiveHighSensitivityPermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        var set = new HashSet<string>(HighSensitivityPermissions.Default, StringComparer.OrdinalIgnoreCase);
        var customs = await customHighSens.AsQueryable().Select(x => x.PermissionCode).ToListAsync();
        foreach (var c in customs)
        {
            if (!string.IsNullOrWhiteSpace(c))
                set.Add(c.Trim());
        }
        return set;
    }

    /// <inheritdoc />
    public virtual async Task<bool> HoldsHighSensitivityPermissionAsync(
        long userId, CancellationToken cancellationToken = default)
    {
        // 超管不走权限码集合,但调用方应先判 IsSuperAdmin;此处仍安全地按码交集判定
        if (users is not null)
        {
            var u = await users.GetByIdAsync(userId);
            if (u?.IsSuperAdmin == true) return true;
        }

        var codes = await permissions.GetPermissionCodesAsync(userId, cancellationToken);
        if (codes.Count == 0) return false;

        var high = await GetEffectiveHighSensitivityPermissionsAsync(cancellationToken);
        foreach (var c in codes)
        {
            if (high.Contains(c)) return true;
        }
        return false;
    }
}
