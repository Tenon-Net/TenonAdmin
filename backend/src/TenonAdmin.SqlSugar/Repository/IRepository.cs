using System.Linq.Expressions;
using SqlSugar;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// 泛型仓储(设计 §2.2/§5.7)——业务服务访问数据库的标准入口,构造注入即用:
/// <code>
/// public class DeviceService(IRepository&lt;Device&gt; repo) : IDeviceService { ... }
/// </code>
/// <para>所有查询自动带全局过滤器(软删除;数据范围过滤器随组织模块接入)。
/// 仓储覆盖不了的复杂操作(联表/事务/批量)直接用 <see cref="Db"/> 上的 SqlSugar 原生能力——
/// 仓储是便捷层,不是把 ORM 关进笼子的抽象层。</para>
/// <para><b>实体不能声明 C# <c>required</c> 成员</b>:本接口(及 <c>ISeedData&lt;TEntity&gt;</c>)的 <c>new()</c>
/// 约束由 SqlSugar 传导——其 <c>Insertable/Updateable/Deleteable/Storageable</c> 的全部泛型入口都要求
/// <c>class, new()</c>,而含 required 成员的类型不满足 new()(CS9040),此约束无法在本层移除
/// (即便移除,C# 重载决议还会把 <c>List&lt;T&gt;</c> 参数静默重绑定到 <c>T=List&lt;T&gt;</c> 的单对象重载,埋运行时错误)。
/// 实体的必填字符串请用非空默认值(<c>= ""</c>)表达,"必填"语义由列 NOT NULL 承载。</para>
/// </summary>
public interface IRepository<TEntity> where TEntity : AuditEntity, new()
{
    /// <summary>底层 SqlSugar 客户端(逃生舱口:联表、事务、Storageable 等原生能力)</summary>
    ISqlSugarClient Db { get; }

    /// <summary>起一个可组合查询(WhereIF/OrderBy/分页等链式操作从这里开始)</summary>
    ISugarQueryable<TEntity> AsQueryable();

    /// <summary>按主键取单条,不存在返回 null</summary>
    Task<TEntity?> GetByIdAsync(long id);

    /// <summary>按条件取第一条,不存在返回 null</summary>
    Task<TEntity?> GetFirstAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>按条件判存在</summary>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>插入单条(Id/CreateTime 由 AOP 自动填充),返回受影响行数</summary>
    Task<int> InsertAsync(TEntity entity);

    /// <summary>批量插入,返回受影响行数</summary>
    Task<int> InsertRangeAsync(List<TEntity> entities);

    /// <summary>整行更新(UpdateTime 由 AOP 自动刷新),返回受影响行数</summary>
    Task<int> UpdateAsync(TEntity entity);

    /// <summary>
    /// 按主键删除,返回受影响行数。<b>按实体能力分流</b>:实体实现 <see cref="ISoftDelete"/>(<see cref="BaseEntity"/> 系)时
    /// 为软删除(置 IsDelete 标记,物理数据保留、查询即不可见,可 <see cref="RestoreAsync"/> 恢复);否则(<see cref="AuditEntity"/> 系,如 <c>OrgAuditEntity</c>)
    /// 为物理删除(行从库中移除)。
    /// </summary>
    Task<int> DeleteAsync(long id);

    /// <summary>按主键物理删除(行从数据库彻底移除)。用于 GDPR 清理、过期数据归档等确需真删的场景。</summary>
    Task<int> HardDeleteAsync(long id);

    /// <summary>
    /// 恢复已软删除的记录(<see cref="DeleteAsync"/> 的逆操作)。自动逆转唯一索引列的 <c>_del_{id}</c> 后缀;
    /// 若逆转后的值与现存记录冲突,抛 <c>RecycleUniqueConflict</c>。
    /// <para>仅对实现 <see cref="ISoftDelete"/> 的实体有意义;对非软删实体(<see cref="AuditEntity"/> 系)抛 <see cref="NotSupportedException"/>(物理删无从恢复)。</para>
    /// </summary>
    Task<int> RestoreAsync(long id);
}
