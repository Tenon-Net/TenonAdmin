using SqlSugar;

namespace TenonAdmin.SqlSugar;

// ponytail: 与 BaseEntity 一样暂放 SqlSugar 层(携带 Sugar 特性)。设计 §5.6 归 Core,待 POCO 化再迁。
/// <summary>
/// 带机构数据范围的实体基类(设计 §5.6/§4 数据权限)。业务表继承它即获得<b>数据范围锚点</b>
/// <see cref="CreateOrgId"/>,配合 T3 的 <c>IDataScopeProvider</c> 全局过滤器,实现"本机构/本机构及以下/仅本人/自定义"隔离。
/// <para>不需要机构隔离的表(如全局字典、机构树自身)继续用 <see cref="BaseEntity"/>。</para>
/// </summary>
public abstract class DataEntity : BaseEntity
{
    /// <summary>
    /// 归属机构 Id(数据范围锚点)= 创建者当时所属机构。由当前用户上下文 AOP 填充(T4 接入),
    /// T3 数据范围过滤器按它 + 当前用户的范围规则决定行可见性。为 null 表示不受机构范围约束(如系统内建数据)。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "归属机构 Id(数据范围锚点)")]
    public long? CreateOrgId { get; set; }
}
