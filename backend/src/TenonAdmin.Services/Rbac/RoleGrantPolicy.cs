using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IRoleGrantPolicy"/> 默认实现。<c>currentUser</c>/<c>dataScope</c> 尾随可选——
/// 未注入(如手工构造的旧测试/消费者精简子类)时视为可信系统上下文,不加限制,行为与批次前一致。
/// </summary>
public class RoleGrantPolicy(
    IRepository<SysRole> roles,
    ICurrentUser? currentUser = null,
    IDataScopeContext? dataScope = null) : IRoleGrantPolicy
{
    /// <inheritdoc />
    public virtual async Task EnsureGrantableAsync(IReadOnlyCollection<long> addedRoleIds, long? targetUserId, long? targetOrgId)
    {
        if (addedRoleIds.Count == 0) return;
        // 超管或系统/未认证上下文(与 IDataScopeContext"未显式设置=可信"同一约定)不受限
        if (currentUser is null || !currentUser.IsAuthenticated || currentUser.IsSuperAdmin) return;

        EnsureTargetInScope(targetUserId, targetOrgId);
        await EnsureRolesDelegatableAsync(addedRoleIds);
    }

    /// <summary>目标用户须在当前数据范围内(按机构匹配;数据范围不受限时恒通过)。</summary>
    protected virtual void EnsureTargetInScope(long? targetUserId, long? targetOrgId)
    {
        var scope = dataScope?.Current ?? DataScopeResult.Unrestricted;
        if (scope.IsUnrestricted) return;

        var inOrgScope = targetOrgId.HasValue && scope.OrgIds.Contains(targetOrgId.Value);
        var isSelf = scope.IncludeSelf && targetUserId.HasValue && targetUserId.Value == scope.UserId;
        AdminException.ThrowIf(!inOrgScope && !isSelf, ErrorCode.UserOutOfDataScope);
    }

    /// <summary>新增的角色须"启用中 + 未删除(全局软删过滤器已排除)+ IsDelegatable == true"。</summary>
    protected virtual async Task EnsureRolesDelegatableAsync(IReadOnlyCollection<long> addedRoleIds)
    {
        var delegatable = await roles.AsQueryable()
            .Where(r => addedRoleIds.Contains(r.Id) && r.Enabled && r.IsDelegatable == true)
            .Select(r => r.Id)
            .ToListAsync();
        AdminException.ThrowIf(addedRoleIds.Except(delegatable).Any(), ErrorCode.RoleNotDelegatable);
    }
}
