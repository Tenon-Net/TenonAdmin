namespace TenonAdmin.Core;

/// <summary>
/// 解析后的<b>生效数据范围</b>(某用户合并其所有角色范围后的最终可见性描述)。
/// 全局查询过滤器据此对 <c>DataEntity</c> 生成 SQL 过滤:
/// <c>Unrestricted</c> → 不过滤;否则 <c>CreateOrgId ∈ OrgIds</c> 或(<c>IncludeSelf</c> 且 <c>CreateUserId == UserId</c>)。
/// <para>不可变值对象——可安全放进缓存跨请求复用(设计 §6 缓存策略)。</para>
/// </summary>
public sealed class DataScopeResult
{
    /// <summary>不受限:看全部数据(All 范围,或系统/可信上下文如启动、种子)</summary>
    public bool IsUnrestricted { get; }

    /// <summary>允许可见的机构 Id 集合(按 CreateOrgId 匹配)</summary>
    public IReadOnlyCollection<long> OrgIds { get; }

    /// <summary>是否附加"仅本人"维度(与机构集合取并集:也能看自己创建的)</summary>
    public bool IncludeSelf { get; }

    /// <summary>当前用户 Id(<see cref="IncludeSelf"/> 为真时用于 CreateUserId 比对)</summary>
    public long UserId { get; }

    private DataScopeResult(bool unrestricted, IReadOnlyCollection<long> orgIds, bool includeSelf, long userId)
    {
        IsUnrestricted = unrestricted;
        OrgIds = orgIds;
        IncludeSelf = includeSelf;
        UserId = userId;
    }

    /// <summary>不受限(看全部)。系统/可信上下文与 All 范围用户共用此值。</summary>
    public static readonly DataScopeResult Unrestricted = new(true, [], false, 0);

    /// <summary>受限范围:给定机构集合 + 可选"仅本人"。两者皆空即"看不到任何数据"(默认拒绝)。</summary>
    public static DataScopeResult Restricted(IReadOnlyCollection<long> orgIds, bool includeSelf, long userId) =>
        new(false, orgIds, includeSelf, userId);
}
