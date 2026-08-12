using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IDictService"/> 默认实现。<see cref="GetItemsByTypeAsync"/> 是前端下拉的热路径读,
/// 走读穿透缓存(命中直接返回,未命中查库回填);字典类型/项的任何增删改都显式失效对应类型的
/// 缓存并广播 <see cref="DictChangedEvent"/>,变更即时生效、订阅者也能感知(如跨节点场景)。
/// </summary>
public class DictService(
    IRepository<SysDictType> types,
    IRepository<SysDictItem> items,
    ICacheProvider cache,
    AdminCacheOptions cacheOptions,
    IEventBus events) : IDictService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<SysDictType>> PageTypesAsync(DictTypePageInput input) =>
        await types.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Code), t => t.Code.Contains(input.Code!))
            .WhereIF(!string.IsNullOrEmpty(input.Name), t => t.Name.Contains(input.Name!))
            .OrderBy(t => t.Sort)
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    public virtual async Task<SysDictType> GetTypeAsync(long id)
    {
        var type = await types.GetByIdAsync(id);
        AdminException.ThrowIf(type is null, ErrorCode.DictTypeNotFound);
        return type!;
    }

    /// <inheritdoc />
    public virtual async Task<long> AddTypeAsync(DictTypeInput input)
    {
        // 查重纳入软删行:唯一索引覆盖已软删行,漏检会撞库唯一约束抛原生 500(P1-10)
        AdminException.ThrowIf(
            await types.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(t => t.Code == input.Code),
            ErrorCode.DictTypeCodeExists);

        var entity = new SysDictType
        {
            Code = input.Code,
            Name = input.Name,
            Sort = input.Sort,
            Enabled = input.Enabled,
            Remark = input.Remark,
        };
        await types.InsertAsync(entity);
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateTypeAsync(long id, DictTypeInput input)
    {
        // Code 创建后不可变:只改 Name/Sort/Enabled/Remark,不动 entity.Code
        var entity = await GetTypeAsync(id);
        entity.Name = input.Name;
        entity.Sort = input.Sort;
        entity.Enabled = input.Enabled;
        entity.Remark = input.Remark;
        await types.UpdateAsync(entity);
        await InvalidateAsync(entity.Code);
    }

    /// <inheritdoc />
    public virtual async Task DeleteTypeAsync(long id)
    {
        var entity = await GetTypeAsync(id);
        AdminException.ThrowIf(entity.Id < 1000, ErrorCode.SeedDataProtected);
        await types.DeleteAsync(id);
        await items.Db.Deleteable<SysDictItem>().Where(i => i.DictTypeCode == entity.Code).ExecuteCommandAsync();
        await InvalidateAsync(entity.Code);
    }

    /// <inheritdoc />
    public virtual async Task DeleteTypesBatchAsync(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return;
        var idList = ids.ToList();
        var targets = await types.AsQueryable().Where(t => idList.Contains(t.Id)).ToListAsync();
        foreach (var t in targets)
        {
            AdminException.ThrowIf(t.Id < 1000, ErrorCode.SeedDataProtected);
        }
        foreach (var t in targets)
        {
            await types.DeleteAsync(t.Id);
            await items.Db.Deleteable<SysDictItem>().Where(i => i.DictTypeCode == t.Code).ExecuteCommandAsync();
            await InvalidateAsync(t.Code);
        }
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<SysDictItem>> GetItemsByTypeAsync(string typeCode)
    {
        var key = CacheKeys.DictItems(typeCode);
        var cached = await cache.GetAsync<List<SysDictItem>>(key);
        if (cached is not null) return cached;

        // 类型不存在或已停用 → 下拉空(与项 Enabled 过滤同口径)。UpdateType 已 Invalidate,不会读到停用前的缓存。
        var type = await types.GetFirstAsync(t => t.Code == typeCode);
        if (type is null || !type.Enabled) return [];

        var list = await items.AsQueryable()
            .Where(i => i.DictTypeCode == typeCode && i.Enabled)
            .OrderBy(i => i.Sort)
            .ToListAsync();

        var ttl = cacheOptions.PermissionMinutes > 0 ? TimeSpan.FromMinutes(cacheOptions.PermissionMinutes) : (TimeSpan?)null;
        await cache.SetAsync(key, list, ttl);
        return list;
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<SysDictItem>> PageItemsAsync(DictItemPageInput input) =>
        await items.AsQueryable()
            .Where(i => i.DictTypeCode == input.TypeCode)
            .OrderBy(i => i.Sort)
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    public virtual async Task<long> AddItemAsync(DictItemInput input)
    {
        AdminException.ThrowIf(
            await items.AsQueryable().ClearFilter<ISoftDelete>()
                .AnyAsync(i => i.DictTypeCode == input.DictTypeCode && i.Value == input.Value),
            ErrorCode.DictItemValueExists);

        var entity = new SysDictItem
        {
            DictTypeCode = input.DictTypeCode,
            Label = input.Label,
            Value = input.Value,
            Sort = input.Sort,
            Enabled = input.Enabled,
        };
        await items.InsertAsync(entity);
        await InvalidateAsync(input.DictTypeCode);
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateItemAsync(long id, DictItemInput input)
    {
        var entity = await items.GetByIdAsync(id);
        AdminException.ThrowIf(entity is null, ErrorCode.DictItemNotFound);

        AdminException.ThrowIf(
            await items.AsQueryable().ClearFilter<ISoftDelete>()
                .AnyAsync(i => i.DictTypeCode == input.DictTypeCode && i.Value == input.Value && i.Id != id),
            ErrorCode.DictItemValueExists);

        var oldTypeCode = entity!.DictTypeCode;
        entity.DictTypeCode = input.DictTypeCode;
        entity.Label = input.Label;
        entity.Value = input.Value;
        entity.Sort = input.Sort;
        entity.Enabled = input.Enabled;
        await items.UpdateAsync(entity);

        // 挂靠类型变了:新旧两个类型的缓存都要失效
        if (oldTypeCode != input.DictTypeCode) await InvalidateAsync(oldTypeCode);
        await InvalidateAsync(input.DictTypeCode);
    }

    /// <inheritdoc />
    public virtual async Task DeleteItemAsync(long id)
    {
        var entity = await items.GetByIdAsync(id);
        AdminException.ThrowIf(entity is null, ErrorCode.DictItemNotFound);
        AdminException.ThrowIf(entity!.Id < 1000, ErrorCode.SeedDataProtected);

        await items.DeleteAsync(id);
        await InvalidateAsync(entity.DictTypeCode);
    }

    /// <inheritdoc />
    public virtual async Task DeleteItemsBatchAsync(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return;
        var idList = ids.ToList();
        var targets = await items.AsQueryable().Where(i => idList.Contains(i.Id)).ToListAsync();
        foreach (var it in targets)
            AdminException.ThrowIf(it.Id < 1000, ErrorCode.SeedDataProtected);
        foreach (var it in targets) await items.DeleteAsync(it.Id);
        foreach (var code in targets.Select(i => i.DictTypeCode).Distinct()) await InvalidateAsync(code);
    }

    /// <summary>失效某字典类型的项缓存,并广播变更事件供订阅者感知。</summary>
    private async Task InvalidateAsync(string typeCode)
    {
        await cache.RemoveAsync(CacheKeys.DictItems(typeCode));
        await events.PublishAsync(new DictChangedEvent(typeCode));
    }
}
