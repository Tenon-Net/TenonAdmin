using System.Globalization;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Integration;

/// <summary>
/// 内置机构范围策略(键 <c>org</c>,实现契约 §4.6):绑定值是机构 Id,<b>绑定某机构即含其全部下级</b>(与内核
/// 「本机构及以下」同一子孙算法)。解析结果写入内核 <see cref="IDataScopeContext"/>,因此开放端点走标准数据路径时——
/// 列表/详情经全局过滤器、按主键改删经仓储写守卫——自动受限,消费者无需自己拼机构条件。
/// <para>子孙按每次请求读取机构表计算(机构调整即时生效,不依赖应用版本)。</para>
/// <para>候选值按调用者的数据范围过滤,与内核机构树同一可见性:非超管只看到范围内的机构及拼树用的祖先。</para>
/// </summary>
public class OrgOpenApiDataScopePolicy(ISqlSugarClient db, ICurrentUser? currentUser = null, IDataScopeContext? dataScope = null)
    : IOpenApiDataScopePolicy
{
    /// <inheritdoc />
    public virtual string Key => OpenApiDataScopes.Org;

    /// <inheritdoc />
    public virtual string Name => "机构";

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<OpenApiScopeOption>> ListOptionsAsync(string? keyword, CancellationToken cancellationToken = default)
    {
        var kw = keyword?.Trim();
        var orgs = await db.Queryable<SysOrg>()
            .WhereIF(!string.IsNullOrEmpty(kw), o => o.Name.Contains(kw!) || o.Code.Contains(kw!))
            .OrderBy(o => o.Sort)
            .OrderBy(o => o.Id)
            .ToListAsync(cancellationToken);
        var visible = await VisibleOrgIdsAsync(cancellationToken);
        return orgs.Where(o => visible is null || visible.Contains(o.Id))
            .Select(o => new OpenApiScopeOption(
                o.Id.ToString(CultureInfo.InvariantCulture),
                o.Name,
                o.ParentId > 0 ? o.ParentId.ToString(CultureInfo.InvariantCulture) : null))
            .ToList();
    }

    /// <summary>
    /// 调用者可见的机构:超管、系统上下文或数据范围不受限时返回 null(全部可见);否则为范围内机构及其祖先(祖先只为拼出完整树)。
    /// </summary>
    protected virtual async Task<HashSet<long>?> VisibleOrgIdsAsync(CancellationToken cancellationToken)
    {
        if (currentUser?.IsSuperAdmin == true) return null;
        var scope = dataScope?.Current;
        if (scope is null || scope.IsUnrestricted) return null;

        var parents = (await db.Queryable<SysOrg>().Select(o => new { o.Id, o.ParentId }).ToListAsync(cancellationToken))
            .ToDictionary(o => o.Id, o => o.ParentId);
        var visible = scope.OrgIds.ToHashSet();
        foreach (var id in scope.OrgIds)
        {
            var current = id;
            while (parents.TryGetValue(current, out var parent) && parent != 0 && visible.Add(parent)) current = parent;
        }
        return visible;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<string>> NormalizeBindingAsync(IReadOnlyCollection<string> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var ids = new List<long>();
        foreach (var raw in values)
        {
            var ok = long.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0;
            IntegrationErrorCode.ThrowIf(!ok, IntegrationErrorCode.ScopeValueInvalid, Args());
            if (!ids.Contains(id)) ids.Add(id);
        }
        IntegrationErrorCode.ThrowIf(ids.Count == 0 || ids.Count > OpenApiDataScopePolicyBase.MaxValues,
            IntegrationErrorCode.ScopeValueInvalid, Args());

        var existing = await db.Queryable<SysOrg>().Where(o => ids.Contains(o.Id)).Select(o => o.Id).ToListAsync(cancellationToken);
        IntegrationErrorCode.ThrowIf(existing.Count != ids.Count, IntegrationErrorCode.ScopeValueInvalid, Args());
        return ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<OpenAppScopeResolution> ResolveAsync(OpenAppScopeBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.AllValues)
            return new OpenAppScopeResolution(new OpenAppDataScope(Key, true, []), DataScopeResult.Unrestricted);

        var roots = binding.Values
            .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
        var rows = await db.Queryable<SysOrg>().Select(o => new { o.Id, o.ParentId }).ToListAsync(cancellationToken);
        var effective = ExpandDescendants(roots, rows.Select(r => (r.Id, r.ParentId)).ToList());

        var scope = new OpenAppDataScope(Key, false, effective.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList());
        return new OpenAppScopeResolution(scope, DataScopeResult.Restricted(effective, includeSelf: false, userId: 0));
    }

    /// <summary>根机构 + 全部子孙(只收仍存在的机构;防御坏数据成环)。</summary>
    protected static HashSet<long> ExpandDescendants(IReadOnlyCollection<long> roots, IReadOnlyCollection<(long Id, long ParentId)> tree)
    {
        var known = tree.Select(n => n.Id).ToHashSet();
        var children = tree.GroupBy(n => n.ParentId).ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
        var result = new HashSet<long>();
        var queue = new Queue<long>(roots.Where(known.Contains));
        foreach (var root in queue) result.Add(root);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!children.TryGetValue(parent, out var kids)) continue;
            foreach (var kid in kids)
                if (result.Add(kid)) queue.Enqueue(kid);
        }
        return result;
    }

    private Dictionary<string, object?> Args() => new() { ["scope"] = Key };
}
