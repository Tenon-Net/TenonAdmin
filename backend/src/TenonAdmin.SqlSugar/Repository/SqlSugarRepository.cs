using System.Linq.Expressions;
using SqlSugar;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// <see cref="IRepository{TEntity}"/> 的 SqlSugar 默认实现。
/// <para>以开放泛型注册(<c>IRepository&lt;&gt;</c> → <c>SqlSugarRepository&lt;&gt;</c>),
/// 任意实体无需逐个注册即可注入。类 public、方法 virtual——遵循框架"继承覆写"承诺(设计 §5.3),
/// 用户可继承本类只改想改的方法,再以 TryAdd 前置注册接管。</para>
/// </summary>
public class SqlSugarRepository<TEntity>(ISqlSugarClient db) : IRepository<TEntity>
    where TEntity : BaseEntity, new()
{
    /// <inheritdoc />
    public ISqlSugarClient Db => db;

    /// <inheritdoc />
    public virtual ISugarQueryable<TEntity> AsQueryable() => db.Queryable<TEntity>();

    /// <inheritdoc />
    public virtual Task<TEntity?> GetByIdAsync(long id) =>
        db.Queryable<TEntity>().Where(e => e.Id == id).FirstAsync()!;

    /// <inheritdoc />
    public virtual Task<TEntity?> GetFirstAsync(Expression<Func<TEntity, bool>> predicate) =>
        db.Queryable<TEntity>().Where(predicate).FirstAsync()!;

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate) =>
        db.Queryable<TEntity>().AnyAsync(predicate);

    /// <inheritdoc />
    public virtual Task<int> InsertAsync(TEntity entity) =>
        db.Insertable(entity).ExecuteCommandAsync();

    /// <inheritdoc />
    public virtual Task<int> InsertRangeAsync(List<TEntity> entities) =>
        db.Insertable(entities).ExecuteCommandAsync();

    /// <inheritdoc />
    public virtual Task<int> UpdateAsync(TEntity entity) =>
        db.Updateable(entity).ExecuteCommandAsync();

    /// <inheritdoc />
    public virtual Task<int> DeleteAsync(long id) =>
        // 软删除:只置标记不删行(全局过滤器随即让该行对查询不可见)。
        // 需要物理删除的场景走 Db.Deleteable<T>() 逃生舱口,属于显式例外。
        db.Updateable<TEntity>()
          .SetColumns(e => e.IsDelete == true)
          .Where(e => e.Id == id)
          .ExecuteCommandAsync();
}
