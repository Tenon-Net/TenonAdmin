using SqlSugar;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// 软删除标记(设计 §12)。实现此接口的实体自动获得全局查询过滤器
/// (<c>IsDelete == false</c>,在 SqlSugarSetup 注册)——已删数据对所有查询天然不可见,
/// 需要查已删数据时用 <c>.ClearFilter&lt;ISoftDelete&gt;()</c> 显式解除。
/// </summary>
public interface ISoftDelete
{
    /// <summary>是否已(软)删除</summary>
    bool IsDelete { get; set; }
}

// ponytail: BaseEntity 暂放 SqlSugar 层(携带 SqlSugar 特性)。
// 设计 §5.6 归 Core;待 Core 改用无特性 POCO + 外部映射时再迁移。
/// <summary>
/// 实体基类(设计 §5.6/§12):主键 + 审计四件套 + 软删除。
/// <para>继承本类即自动获得(由 SqlSugarSetup 的 AOP 钩子驱动,业务代码零感知):</para>
/// <list type="bullet">
///   <item>插入时 Id==0 → 自动填雪花 ID(<see cref="Core.IIdGenerator"/>);显式给定的 Id(如种子数据)原样保留</item>
///   <item>插入时 CreateTime 未设置 → 自动填当前时间;更新时 UpdateTime 总是刷新</item>
///   <item>CreateUserId / UpdateUserId 由当前登录用户上下文填充(认证闭环接入后生效)</item>
///   <item>查询自动过滤 IsDelete == true 的行(全局软删过滤器)</item>
/// </list>
/// <para>带机构数据范围的业务实体应继承 <c>DataEntity</c>(随组织模块引入,含机构字段)。</para>
/// </summary>
public abstract class BaseEntity : ISoftDelete
{
    [SugarColumn(IsPrimaryKey = true, ColumnDescription = "主键(雪花 ID;种子数据用固定小整数)")]
    public long Id { get; set; }

    [SugarColumn(ColumnDescription = "创建时间")]
    public DateTime CreateTime { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "创建人用户 Id")]
    public long? CreateUserId { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "最后更新时间")]
    public DateTime? UpdateTime { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "最后更新人用户 Id")]
    public long? UpdateUserId { get; set; }

    [SugarColumn(ColumnDescription = "软删除标记")]
    public bool IsDelete { get; set; }
}
