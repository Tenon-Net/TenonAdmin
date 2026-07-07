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
/// <para><b>写路径提示(P2-21)</b>:全局过滤器只作用于<b>查询(SELECT)</b>,不作用于按主键的
/// <c>Updateable</c>/<c>Deleteable</c>。故对 DataEntity 业务表,<b>改/删授权非自动</b>:直接
/// <c>repo.DeleteAsync(id)</c>/<c>UpdateAsync(dto)</c> 不带范围谓词,可越权改删他机构的行(IDOR)。
/// 安全范式是"先经 <c>AsQueryable</c> 过滤读到(看不到即拒)、再写"——内置服务均如此。业务端点务必遵循,
/// 或在写前显式校验归属。机制级兜底(写路径自动补范围谓词)留待首张业务表落地时评估。</para>
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
