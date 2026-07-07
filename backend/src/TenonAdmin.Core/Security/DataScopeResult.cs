namespace TenonAdmin.Core;

/// <summary>
/// 解析后的<b>生效数据范围</b>(某用户合并其所有角色范围后的最终可见性描述)。
/// 全局查询过滤器据此对 <c>DataEntity</c> 生成 SQL 过滤:
/// <c>Unrestricted</c> → 不过滤;否则 <c>CreateOrgId ∈ OrgIds</c> 或(<c>IncludeSelf</c> 且 <c>CreateUserId == UserId</c>)。
/// <para>不可变值对象——可安全放进缓存跨请求复用(设计 §6 缓存策略)。用 <c>record</c> + <c>init</c> 属性,
/// 参数名与属性名对齐、有公共构造器,序列化友好:装 <c>RedisCacheProvider</c> 做多实例时能被 System.Text.Json
/// 正常往返(旧版仅私有构造器 + 名字不齐,STJ 反序列化会抛 NotSupportedException,P2-10)。</para>
/// </summary>
public sealed record DataScopeResult
{
    /// <summary>不受限:看全部数据(All 范围,或系统/可信上下文如启动、种子)</summary>
    public bool IsUnrestricted { get; init; }

    /// <summary>允许可见的机构 Id 集合(按 CreateOrgId 匹配)</summary>
    public IReadOnlyCollection<long> OrgIds { get; init; } = [];

    /// <summary>是否附加"仅本人"维度(与机构集合取并集:也能看自己创建的)</summary>
    public bool IncludeSelf { get; init; }

    /// <summary>当前用户 Id(<see cref="IncludeSelf"/> 为真时用于 CreateUserId 比对)</summary>
    public long UserId { get; init; }

    /// <summary>不受限(看全部)。系统/可信上下文与 All 范围用户共用此值。</summary>
    public static readonly DataScopeResult Unrestricted = new() { IsUnrestricted = true };

    /// <summary>受限范围:给定机构集合 + 可选"仅本人"。两者皆空即"看不到任何数据"(默认拒绝)。</summary>
    public static DataScopeResult Restricted(IReadOnlyCollection<long> orgIds, bool includeSelf, long userId) =>
        new() { IsUnrestricted = false, OrgIds = orgIds, IncludeSelf = includeSelf, UserId = userId };
}
