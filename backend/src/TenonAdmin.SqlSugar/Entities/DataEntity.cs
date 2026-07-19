using SqlSugar;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// 数据范围标记接口——全局过滤器按<b>接口</b>而非基类匹配实体
/// (SqlSugar 的 <c>AddTableFilter&lt;T&gt;</c> 匹配接口/精确类型,不匹配基类;软删过滤器同理走 <see cref="ISoftDelete"/>)。
/// 暴露锚点字段 <see cref="CreateOrgId"/>(归属机构)与 <see cref="CreateUserId"/>(创建者,供"仅本人")。
/// </summary>
public interface IOrgScoped
{
    /// <summary>归属机构 Id(数据范围锚点)</summary>
    long? CreateOrgId { get; }

    /// <summary>创建人用户 Id(“仅本人”范围据此比对;定义在 <see cref="BaseEntity"/>)</summary>
    long? CreateUserId { get; }
}

// ponytail: 与 BaseEntity 一样暂放 SqlSugar 层(携带 Sugar 特性)。设计 §5.6 归 Core,待 POCO 化再迁。
/// <summary>
/// 带机构数据范围的实体基类(设计 §5.6/§4 数据权限)。业务表继承它即获得<b>数据范围锚点</b>
/// <see cref="CreateOrgId"/>,配合 T3 的 <c>IDataScopeProvider</c> 全局过滤器(按 <see cref="IOrgScoped"/> 匹配),
/// 实现"本机构/本机构及以下/仅本人/自定义"隔离。
/// <para>不需要机构隔离的表(如全局字典、机构树自身)继续用 <see cref="BaseEntity"/>。</para>
/// <para><b>写路径守卫(P2-21)</b>:数据范围全局过滤器只作用于<b>查询(SELECT)</b>,不作用于按主键的
/// <c>Updateable</c>/<c>Deleteable</c>。为此 <see cref="SqlSugarRepository{TEntity}"/> 对 IOrgScoped 实体的
/// <c>UpdateAsync</c>/<c>DeleteAsync</c> <b>已内置写路径范围守卫</b>:写前经带范围过滤器的查询确认目标行在当前
/// 数据范围内,越权改删他机构行会被拒(返回 0 行),<b>默认安全</b>。仍建议业务服务改/删前先 <c>GetByIdAsync</c>
/// (经范围过滤)校验存在,以返回准确的"未找到/无权"(内置服务均如此)。绕过仓储直接走 <c>Db.Updateable/Deleteable</c>
/// 逃生舱口的写不受此守卫,属显式例外,需自行校验归属。</para>
/// </summary>
public abstract class DataEntity : BaseEntity, IOrgScoped
{
    /// <summary>
    /// 归属机构 Id(数据范围锚点)= 创建者当时所属机构。插入时由审计 AOP 从 <c>ICurrentUser.OrgId</c>(令牌 org claim)自动填充,
    /// T3 数据范围过滤器按它 + 当前用户的范围规则决定行可见性。为 null 表示不受机构范围约束(系统内建数据、或创建者无归属机构)。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "归属机构 Id(数据范围锚点)")]
    public long? CreateOrgId { get; set; }
}

/// <summary>
/// 带机构数据范围的<b>审计实体</b>(#10):<see cref="AuditEntity"/> + <see cref="IOrgScoped"/>,<b>不含软删除</b>——
/// <see cref="DataEntity"/> 的“物理删版”。业务表继承它即获得数据范围锚点 <see cref="CreateOrgId"/>(同 <c>DataEntity</c>,
/// 全局过滤器与写路径守卫都按 <see cref="IOrgScoped"/> 接口生效),但仓储 <c>DeleteAsync</c> 是<b>物理删除</b>、无回收站。
/// <para>适用于确需真删、又要机构隔离 + 审计的业务表。需要软删除/回收站的机构表继续用 <see cref="DataEntity"/>。</para>
/// <para><b>写路径守卫(P2-21)</b>同 <see cref="DataEntity"/>:<see cref="SqlSugarRepository{TEntity}"/> 对 <see cref="IOrgScoped"/>
/// 实体的 <c>UpdateAsync</c>/<c>DeleteAsync</c> 已内置范围守卫,越权改删他机构行被拒(返回 0 行)。</para>
/// </summary>
public abstract class OrgAuditEntity : AuditEntity, IOrgScoped
{
    /// <summary>
    /// 归属机构 Id(数据范围锚点)= 创建者当时所属机构。插入时由审计 AOP 从 <c>ICurrentUser.OrgId</c> 自动填充(按 <see cref="IOrgScoped"/> 匹配),
    /// 数据范围过滤器据它决定行可见性。为 null 表示不受机构范围约束。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "归属机构 Id(数据范围锚点)")]
    public long? CreateOrgId { get; set; }
}
