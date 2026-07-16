using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IModuleService"/> 默认实现。编码唯一查重纳入软删行(与 <c>OrgService</c> 同规矩,
/// 避免撞库上唯一索引抛原生 500)。内置 system 模块按固定 Id 保护,不可删除、不可停用。
/// </summary>
public class ModuleService(IRepository<SysModule> modules, ICacheProvider cache) : IModuleService
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<SysModule>> ListAsync() =>
        await modules.AsQueryable().OrderBy(m => m.Sort).OrderBy(m => m.Id).ToListAsync();

    /// <inheritdoc />
    public virtual async Task<SysModule> GetAsync(long id)
    {
        var module = await modules.GetByIdAsync(id);
        AdminException.ThrowIf(module is null, ErrorCode.ModuleNotFound);
        return module!;
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(ModuleInput input)
    {
        AdminException.ThrowIf(
            await modules.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(m => m.Code == input.Code),
            ErrorCode.ModuleCodeExists);

        var entity = new SysModule
        {
            Code = input.Code,
            Title = input.Title,
            Icon = input.Icon,
            DefaultRoute = input.DefaultRoute,
            ApiPrefix = input.ApiPrefix,
            Sort = input.Sort,
            Enabled = input.Enabled,
            Remark = input.Remark,
        };
        await modules.InsertAsync(entity);
        await cache.IncrementAsync(CacheKeys.PortalGeneration);   // 新模块改门户模块列表(超管即见)→ 门户缓存整体失效
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, ModuleInput input)
    {
        // 内置 system 模块承载全部管理页:停用即门户失联且无 UI 恢复入口,比照删除拒绝(按固定 Id 判)。
        AdminException.ThrowIf(id == DefaultModuleSeed.BUILTIN_MODULE_ID && !input.Enabled, ErrorCode.ModuleProtected);
        var entity = await GetAsync(id);
        AdminException.ThrowIf(
            input.Code != entity.Code &&
            await modules.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(m => m.Code == input.Code && m.Id != id),
            ErrorCode.ModuleCodeExists);

        entity.Code = input.Code;
        entity.Title = input.Title;
        entity.Icon = input.Icon;
        entity.DefaultRoute = input.DefaultRoute;
        entity.ApiPrefix = input.ApiPrefix;
        entity.Sort = input.Sort;
        entity.Enabled = input.Enabled;
        entity.Remark = input.Remark;
        await modules.UpdateAsync(entity);
        await cache.IncrementAsync(CacheKeys.PortalGeneration);   // 标题/图标/排序/启用变更改门户模块列表 → 门户缓存整体失效
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        // 内置 system 模块受保护(按固定 Id 判,不随 Code 改动而失守)。
        AdminException.ThrowIf(id == DefaultModuleSeed.BUILTIN_MODULE_ID, ErrorCode.ModuleProtected);
        await GetAsync(id);   // 不存在则抛 ModuleNotFound
        // 有挂靠菜单则拒删(比照 MenuHasChildren):删了模块,其顶级目录 ModuleId 悬空、整棵子树从门户消失,
        // 让用户先迁移或删除挂靠的顶级目录。走 Db 逃生舱查菜单而不给主构造器加 IRepository<SysMenu>——
        // 加构造参数对继承本类的消费者是源码破坏性变更(同 PersonalService.OrgNameAsync 的取舍)。
        AdminException.ThrowIf(
            await modules.Db.Queryable<SysMenu>().AnyAsync(m => m.ModuleId == id),
            ErrorCode.ModuleHasMenus);
        await modules.DeleteAsync(id);
        await cache.IncrementAsync(CacheKeys.PortalGeneration);   // 删模块改门户模块列表 → 门户缓存整体失效
    }
}
