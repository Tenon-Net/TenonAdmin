using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// <see cref="IPermissionProvider"/> 的占位默认实现:恒返回空集合——
/// <b>非超管用户对一切 [RolePermission] 接口默认 403(拒绝优先,设计 §14 授权默认拒绝)</b>。
/// RBAC 模块(角色-菜单-权限码,M1 后续纵切)接入时替换为"从缓存读用户权限码集合"的实现;
/// 对接外部鉴权中心的用户也是替换它。
/// </summary>
internal sealed class DefaultPermissionProvider : IPermissionProvider
{
    private static readonly IReadOnlyCollection<string> EMPTY = [];

    public Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(long userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(EMPTY);
}
