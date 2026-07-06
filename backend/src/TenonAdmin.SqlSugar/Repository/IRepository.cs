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
/// </summary>
public interface IRepository<TEntity> where TEntity : BaseEntity, new()
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

    /// <summary>按主键软删除(置 IsDelete 标记,物理数据保留、查询即不可见),返回受影响行数</summary>
    Task<int> DeleteAsync(long id);
}
