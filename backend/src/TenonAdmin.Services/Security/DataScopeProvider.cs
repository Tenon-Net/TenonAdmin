using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IDataScopeProvider"/> 默认实现:合并用户各角色的数据范围,结果按用户缓存。
/// <para>合并规则(取最宽):任一角色 <see cref="DataScopeType.All"/> → 不受限;
/// 否则并集各角色的机构集合(本机构=主属机构;本机构及以下=主属机构+子孙;自定义=指定集合),
/// 任一角色 <see cref="DataScopeType.Self"/> 则附加"仅本人"。</para>
/// <para>边界:无角色 → 仅本人;有角色但未配任何范围 → 仅本人(安全默认,不放大可见面)。</para>
/// </summary>
public class DataScopeProvider(
    IRepository<SysUser> users,
    IRepository<SysUserRole> userRoles,
    IRepository<SysRoleDataScope> roleScopes,
    IRepository<SysOrg> orgs,
    ICacheProvider cache,
    AdminCacheOptions cacheOptions) : IDataScopeProvider
{
    /// <inheritdoc />
    public virtual async Task<DataScopeResult> ResolveAsync(long userId, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.UserDataScope(userId);
        var cached = await cache.GetAsync<DataScopeResult>(key, cancellationToken);
        if (cached is not null) return cached;

        var result = await ComputeAsync(userId);
        var ttl = cacheOptions.PermissionMinutes > 0 ? TimeSpan.FromMinutes(cacheOptions.PermissionMinutes) : (TimeSpan?)null;
        await cache.SetAsync(key, result, ttl, cancellationToken);
        return result;
    }

    /// <summary>聚合查库计算生效范围(仅缓存未命中时执行)。</summary>
    protected virtual async Task<DataScopeResult> ComputeAsync(long userId)
    {
        var roleIds = await userRoles.AsQueryable().Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync();
        if (roleIds.Count == 0) return DataScopeResult.Restricted([], includeSelf: true, userId); // 无角色 → 仅本人

        var scopes = await roleScopes.AsQueryable().Where(x => roleIds.Contains(x.RoleId)).ToListAsync();
        if (scopes.Any(s => s.ScopeType == DataScopeType.All)) return DataScopeResult.Unrestricted;
        if (scopes.Count == 0) return DataScopeResult.Restricted([], includeSelf: true, userId); // 有角色未配范围 → 仅本人

        var userOrgId = (await users.GetByIdAsync(userId))?.OrgId;
        var orgSet = new HashSet<long>();
        var includeSelf = false;
        List<SysOrg>? orgTree = null;   // 惰性加载:仅"本机构及以下"才需要整棵树

        foreach (var scope in scopes)
        {
            switch (scope.ScopeType)
            {
                case DataScopeType.Org:
                    if (userOrgId is { } o1) orgSet.Add(o1);
                    break;
                case DataScopeType.OrgAndChildren:
                    if (userOrgId is { } o2)
                    {
                        orgSet.Add(o2);
                        orgTree ??= await orgs.AsQueryable().ToListAsync();
                        CollectDescendants(o2, orgTree, orgSet);
                    }
                    break;
                case DataScopeType.Self:
                    includeSelf = true;
                    break;
                case DataScopeType.Custom:
                    foreach (var id in ParseOrgIds(scope.CustomOrgIds)) orgSet.Add(id);
                    break;
            }
        }

        return DataScopeResult.Restricted(orgSet, includeSelf, userId);
    }

    /// <summary>把 root 的所有子孙机构 Id 收进 <paramref name="into"/>(广度优先,防御性去重避免坏数据成环死循环)。</summary>
    private static void CollectDescendants(long root, List<SysOrg> allOrgs, HashSet<long> into)
    {
        var queue = new Queue<long>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var child in allOrgs.Where(o => o.ParentId == parent))
                if (into.Add(child.Id)) queue.Enqueue(child.Id);   // Add 返回 false(已收过)即不再入队
        }
    }

    /// <summary>解析逗号分隔的机构 Id 串(忽略空白与非法项)。</summary>
    private static IEnumerable<long> ParseOrgIds(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => long.TryParse(s, out var id) ? id : (long?)null)
                 .Where(id => id.HasValue)
                 .Select(id => id!.Value);
}
