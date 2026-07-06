namespace TenonAdmin.Core;

/// <summary>
/// 数据范围类型(设计 §4 数据权限,招牌能力)——角色可见数据的机构维度。存库为 int。
/// <para>多角色取<b>最宽</b>:任一角色为 <see cref="All"/> 即看全部;否则并集各角色的机构集合。</para>
/// </summary>
public enum DataScopeType
{
    /// <summary>全部数据(不受机构约束)</summary>
    All = 1,

    /// <summary>本机构(仅用户主属机构)</summary>
    Org = 2,

    /// <summary>本机构及以下(用户主属机构 + 其所有子孙机构)</summary>
    OrgAndChildren = 3,

    /// <summary>仅本人(只看自己创建的数据)</summary>
    Self = 4,

    /// <summary>自定义机构(显式指定的机构集合)</summary>
    Custom = 5,
}
