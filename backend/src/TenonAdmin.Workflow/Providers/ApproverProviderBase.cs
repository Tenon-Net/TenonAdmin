using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>内置 Provider 公共步:启用用户过滤、按 Id 取用户。方法 <c>virtual</c> 便于子类覆写单步。</summary>
public abstract class ApproverProviderBase(IRepository<SysUser> users) : IApproverProvider
{
    /// <summary>用户仓储(子类查询复用,避免再捕获主构造参数触发 CS9107)。</summary>
    protected IRepository<SysUser> Users { get; } = users;

    public abstract string Key { get; }

    public abstract Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default);

    /// <summary>去掉停用/不存在用户,保序去重。</summary>
    protected virtual async Task<IReadOnlyList<long>> FilterEnabledAsync(
        IEnumerable<long> userIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ordered = userIds.Where(id => id > 0).Distinct().ToList();
        if (ordered.Count == 0)
            return [];

        var enabled = await Users.AsQueryable()
            .Where(u => ordered.Contains(u.Id) && u.Enabled)
            .Select(u => u.Id)
            .ToListAsync();

        var set = enabled.ToHashSet();
        return ordered.Where(set.Contains).ToList();
    }

    /// <summary>按主键取用户;不存在返回 <c>null</c>。</summary>
    protected virtual Task<SysUser?> GetUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Users.GetByIdAsync(userId);
    }
}
