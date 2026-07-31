using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IHighSensitivityPermissionService"/> 默认实现。
/// 默认集只读;自定义码唯一索引;删除禁止默认码。
/// </summary>
public class HighSensitivityPermissionService(
    IRepository<SysHighSensitivityPermission> repo,
    IRepository<SysUser> users,
    AdminSecurityOptions? security = null,
    IPermissionProvider? permissions = null,
    ISecurityProfileAccessor? profile = null,
    ILogger<HighSensitivityPermissionService>? logger = null) : IHighSensitivityPermissionService
{
    private bool TotpOn =>
        security?.IsTotpFeatureEnabled == true
        || profile?.IsLevel3 == true;

    /// <inheritdoc />
    public virtual async Task<HighSensitivityPermissionList> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var customs = await repo.AsQueryable()
            .OrderBy(x => x.PermissionCode)
            .ToListAsync();
        return new HighSensitivityPermissionList
        {
            Defaults = HighSensitivityPermissions.Default.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Customs = customs.Select(c => new HighSensitivityPermissionItem
            {
                Id = c.Id,
                PermissionCode = c.PermissionCode,
                Remark = c.Remark,
            }).ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task<SysHighSensitivityPermission> AddAsync(
        HighSensitivityPermissionInput input, long operatorUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(operatorUserId, HighSensitivityPermissions.HighSensAdd);

        var code = (input.PermissionCode ?? "").Trim();
        AdminException.ThrowIf(string.IsNullOrEmpty(code), ErrorCode.ConfigNotFound);
        // 默认集不可追加(防管理页「删除默认」错觉);重复追加拒
        AdminException.ThrowIf(HighSensitivityPermissions.IsDefault(code), ErrorCode.NoPermission);

        var exists = await repo.GetFirstAsync(x => x.PermissionCode == code);
        AdminException.ThrowIf(exists is not null, ErrorCode.NoPermission);

        var row = new SysHighSensitivityPermission
        {
            PermissionCode = code,
            Remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim(),
        };
        await repo.InsertAsync(row);
        logger?.LogInformation(
            "High-sensitivity custom permission added: code={Code} by={Operator}",
            code, operatorUserId);
        return row;
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id, long operatorUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(operatorUserId, HighSensitivityPermissions.HighSensDelete);

        var row = await repo.GetByIdAsync(id);
        AdminException.ThrowIf(row is null, ErrorCode.ConfigNotFound);
        AdminException.ThrowIf(
            HighSensitivityPermissions.IsDefault(row!.PermissionCode),
            ErrorCode.NoPermission);

        await repo.DeleteAsync(row.Id);
        logger?.LogInformation(
            "High-sensitivity custom permission deleted: id={Id} code={Code} by={Operator}",
            id, row.PermissionCode, operatorUserId);
    }

    protected virtual async Task EnsureOperatorAsync(long operatorUserId, string permissionCode)
    {
        AdminException.ThrowIf(operatorUserId <= 0, ErrorCode.NoPermission);
        var op = await users.GetByIdAsync(operatorUserId);
        AdminException.ThrowIf(op is null || !op!.Enabled, ErrorCode.NoPermission);
        if (TotpOn)
            AdminException.ThrowIf(!op.TotpEnabled, ErrorCode.TotpNotBound);
        if (op.IsSuperAdmin) return;
        if (permissions is null)
            throw new AdminException(ErrorCode.NoPermission);
        var codes = await permissions.GetPermissionCodesAsync(operatorUserId);
        AdminException.ThrowIf(
            !codes.Contains(permissionCode, StringComparer.OrdinalIgnoreCase),
            ErrorCode.NoPermission);
    }
}
