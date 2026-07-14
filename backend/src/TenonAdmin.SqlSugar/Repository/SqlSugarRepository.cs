using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// <see cref="IRepository{TEntity}"/> 的 SqlSugar 默认实现。
/// <para>以开放泛型注册(<c>IRepository&lt;&gt;</c> → <c>SqlSugarRepository&lt;&gt;</c>),
/// 任意实体无需逐个注册即可注入。类 public、方法 virtual——遵循框架"继承覆写"承诺(设计 §5.3),
/// 用户可继承本类只改想改的方法,再以 TryAdd 前置注册接管。</para>
/// <para><paramref name="time"/> / <paramref name="currentUser"/> <b>必须保持可选参数</b>:本类是消费者可继承的
/// public 类型(§8 六件套契约),加必需构造参数就是源码破坏性变更——现有 <c>: SqlSugarRepository&lt;T&gt;(db)</c>
/// 的子类会编译不过。两者只在软删审计留痕时用到,缺省即回退系统时钟 / 无操作人。</para>
/// </summary>
public class SqlSugarRepository<TEntity>(ISqlSugarClient db, TimeProvider? time = null, ICurrentUser? currentUser = null)
    : IRepository<TEntity>
    where TEntity : BaseEntity, new()
{
    /// <inheritdoc />
    public ISqlSugarClient Db => db;

    // 编译期判定实体是否受机构数据范围约束(实现 IOrgScoped,即 DataEntity 子类)。
    // 用于写路径越权兜底:全局范围过滤器只作用于查询(SELECT),不作用于按主键的 Update/Delete。
    private static readonly bool IsOrgScoped = typeof(IOrgScoped).IsAssignableFrom(typeof(TEntity));

    // 每个实体类型的唯一索引字符串列(运行时从 SugarIndex 元数据解析,缓存一次)
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> UniqueStringColumnsCache = new();

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

        var uniqueCols = GetUniqueStringColumns();
        if (uniqueCols.Length > 0)
        {
            var result = await db.Ado.UseTranAsync(async () =>
            {
                await ReleaseUniqueColumnsAsync(id, uniqueCols);
                await SoftDeleteCoreAsync(id);
            });
            if (!result.IsSuccess) throw result.ErrorException;
            return 1;
        }

        return await SoftDeleteCoreAsync(id);
    }

    /// <inheritdoc />
    public virtual async Task<int> HardDeleteAsync(long id)
    {
        if (!await InScopeAsync(id)) return 0;
        return await db.Deleteable<TEntity>().In(id).ExecuteCommandAsync();
    }

    /// <inheritdoc />
    public virtual async Task<int> RestoreAsync(long id)
    {
        var entity = await db.Queryable<TEntity>().ClearFilter<ISoftDelete>()
            .Where(e => e.Id == id && e.IsDelete == true).FirstAsync();
        if (entity is null) return 0;

        var uniqueCols = GetUniqueStringColumns();
        var suffix = $"_del_{id}";

        if (uniqueCols.Length > 0)
        {
            // 逆转唯一列的 _del_{id} 后缀
            foreach (var prop in uniqueCols)
            {
                var val = (string?)prop.GetValue(entity) ?? "";
                if (val.EndsWith(suffix))
                    prop.SetValue(entity, val[..^suffix.Length]);
            }
            // 冲突检查:去后缀后的值是否已被现存记录占用
            foreach (var prop in uniqueCols)
            {
                var restored = (string?)prop.GetValue(entity) ?? "";
                var colName = prop.Name;
                var conflict = await db.Queryable<TEntity>()
                    .Where(e => e.Id != id)
                    .Where($"{colName} = @v", new { v = restored })
                    .AnyAsync();
                if (conflict)
                    throw new AdminException(Core.ErrorCode.RecycleUniqueConflict);
            }

            var result = await db.Ado.UseTranAsync(async () =>
            {
                await db.Updateable(entity)
                    .UpdateColumns(uniqueCols.Select(p => p.Name).ToArray())
                    .Where(e => e.Id == id)
                    .ExecuteCommandAsync();
                await db.Updateable<TEntity>()
                    .SetColumns(e => e.IsDelete == false)
                    .Where(e => e.Id == id)
                    .ExecuteCommandAsync();
            });
            if (!result.IsSuccess) throw result.ErrorException;
            return 1;
        }

        return await db.Updateable<TEntity>()
            .SetColumns(e => e.IsDelete == false)
            .Where(e => e.Id == id)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 软删除核心:置 IsDelete 标记 + 审计时间/操作人。提取为独立方法以便 <see cref="DeleteAsync"/> 在
    /// 需要释放唯一列时将其与释放操作包进同一个事务,不需要时则直接调用(零事务开销)。
    /// </summary>
    protected virtual async Task<int> SoftDeleteCoreAsync(long id)
    {
        // 审计两列必须在这里显式置:删也是一次更新,"谁、什么时候删的"是审计的基本要求,
        // 而按列更新(SetColumns)走的不是整对象更新路径,SqlSugarSetup 里那个只认 UpdateByObject 的
        // 审计 AOP 根本不会触发 —— 不显式写,UpdateTime/UpdateUserId 就永远停在删除之前的值。
        // 删除时间同时是文件回收任务的保留期锚点(设计 §12 / dev-plan T-D2),丢了它 GC 无从判断该不该收。
        var now = (time ?? TimeProvider.System).GetLocalNow().DateTime;   // 与审计 AOP 同一时间口径
        var update = db.Updateable<TEntity>()
          .SetColumns(e => e.IsDelete == true)
          .SetColumns(e => e.UpdateTime == now);
        if (currentUser?.UserId is { } uid)                               // 无登录上下文(系统/后台任务)则不硬塞操作人
            update = update.SetColumns(e => e.UpdateUserId == uid);

        return await update.Where(e => e.Id == id).ExecuteCommandAsync();
    }

    /// <summary>
    /// 取当前实体类型中所有受唯一索引约束的 string 列(从 <see cref="SugarIndexAttribute"/> 元数据解析,按类型缓存)。
    /// 软删前需将这些列的值追加 <c>_del_{id}</c> 后缀,释放唯一约束占位,使原值可被新行复用。
    /// </summary>
    private PropertyInfo[] GetUniqueStringColumns()
    {
        return UniqueStringColumnsCache.GetOrAdd(typeof(TEntity), _ =>
        {
            var info = db.EntityMaintenance.GetEntityInfo<TEntity>();
            // SqlSugar 对无索引实体返回 Indexs == null(而非空集),不 guard 会 NRE
            var uniquePropNames = (info.Indexs ?? [])
                .Where(idx => idx.IsUnique)
                .SelectMany(idx => idx.IndexFields.Keys)
                .ToHashSet(StringComparer.Ordinal);

            return info.Columns
                .Where(c => uniquePropNames.Contains(c.PropertyName) && c.PropertyInfo.PropertyType == typeof(string))
                .Select(c => c.PropertyInfo)
                .ToArray();
        });
    }

    /// <summary>软删前释放唯一索引字符串列:读出当前值,追加 <c>_del_{id}</c>,按列更新回去。</summary>
    private async Task ReleaseUniqueColumnsAsync(long id, PropertyInfo[] uniqueCols)
    {
        var entity = await db.Queryable<TEntity>().InSingleAsync(id);
        if (entity is null) return;

        foreach (var prop in uniqueCols)
            prop.SetValue(entity, ((string?)prop.GetValue(entity) ?? "") + $"_del_{id}");

        await db.Updateable(entity)
            .UpdateColumns(uniqueCols.Select(p => p.Name).ToArray())
            .Where(e => e.Id == id)
            .ExecuteCommandAsync();
    }
}
