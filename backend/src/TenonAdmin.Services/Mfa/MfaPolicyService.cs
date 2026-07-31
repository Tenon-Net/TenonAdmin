using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IMfaPolicyService"/> 默认实现。
/// <para>ADR 0006：强制 TOTP 仅在 <c>Security:Totp:Enabled</c>（或过渡期 Profile=Level3）下生效。
/// 产品规则：账号 <c>ForceTotp</c>；可选 <c>Totp:RequireForSuperAdmin</c>。
/// 历史 Level3 的「高敏权限自动强制」仅在过渡期 Profile=Level3 时保留。</para>
/// </summary>
public class MfaPolicyService(
    IPermissionProvider permissions,
    IRepository<SysHighSensitivityPermission> customHighSens,
    AdminSecurityOptions security,
    ISecurityProfileAccessor? profile = null,
    IRepository<SysUser>? users = null) : IMfaPolicyService
{
    /// <inheritdoc />
    public virtual async Task<bool> IsMfaRequiredAsync(SysUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!security.IsTotpFeatureEnabled) return false;

        if (user.ForceTotp) return true;
        if (user.IsSuperAdmin && security.Totp.RequireForSuperAdmin) return true;

        // 过渡：历史 Level3 总档仍自动强制超管 + 高敏权限持有者
        var legacyLevel3 = profile?.IsLevel3 == true || security.IsLegacyLevel3Profile;
        if (legacyLevel3)
        {
            if (user.IsSuperAdmin) return true;
            return await HoldsHighSensitivityPermissionAsync(user.Id, cancellationToken);
        }

        return false;
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
