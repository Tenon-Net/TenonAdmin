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

    // 编译期判定实体是否受机构数据范围约束(实现 IOrgScoped,即 DataEntity 子类)。
    // 用于写路径越权兜底:全局范围过滤器只作用于查询(SELECT),不作用于按主键的 Update/Delete。
    private static readonly bool IsOrgScoped = typeof(IOrgScoped).IsAssignableFrom(typeof(TEntity));

    /// <inheritdoc />
    public virtual ISugarQueryable<TEntity> AsQueryable() => db.Queryable<TEntity>();

    /// <summary>
    /// IOrgScoped 实体的写前范围守卫:经带全局范围过滤器的查询确认目标行在当前数据范围内。
    /// 复用已注册的查询过滤器(<c>SqlSugarSetup</c> 的 <c>AddTableFilter&lt;IOrgScoped&gt;</c>),
    /// 不在范围内(或不存在)即返回 false,调用方据此拒写——堵住按主键改删他机构行的 IDOR(P2-21)。
    /// 普通 BaseEntity(<c>IsOrgScoped==false</c>)恒真短路,无额外查询、行为不变。
    /// </summary>
    protected virtual async Task<bool> InScopeAsync(long id) =>
        !IsOrgScoped || await db.Queryable<TEntity>().Where(e => e.Id == id).AnyAsync();

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
    public virtual async Task<int> UpdateAsync(TEntity entity)
    {
        if (!await InScopeAsync(entity.Id)) return 0;   // 越权改防护(仅 IOrgScoped 实体触发实际检查)
        return await db.Updateable(entity).ExecuteCommandAsync();
    }

    /// <inheritdoc />
    public virtual async Task<int> DeleteAsync(long id)
    {
        if (!await InScopeAsync(id)) return 0;   // 越权删防护(仅 IOrgScoped 实体触发实际检查)
        // 软删除:只置标记不删行(全局过滤器随即让该行对查询不可见)。
        // 需要物理删除的场景走 Db.Deleteable<T>() 逃生舱口,属于显式例外。
        return await db.Updateable<TEntity>()
          .SetColumns(e => e.IsDelete == true)
          .Where(e => e.Id == id)
          .ExecuteCommandAsync();
    }
}
