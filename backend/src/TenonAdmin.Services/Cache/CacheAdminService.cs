using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <inheritdoc cref="ICacheAdminService"/>
public class CacheAdminService(
    ICacheProvider cache,
    IRepository<SysUser> users,
    IRepository<SysDictType> dictTypes,
    IRepository<SysConfig> configs) : ICacheAdminService
{
    /// <inheritdoc />
    public virtual async Task<int> FlushAuthAsync(CancellationToken cancellationToken = default)
    {
        // ponytail: O(用户数),显式管理员级刷新可接受。perm/scope 是 per-user 键、无代际计数器,
        // 只能逐用户键移除(其余靠 TTL 自然回收);SysUser 非机构范围实体,查询即得全量存活用户
        var ids = await users.AsQueryable().Select(u => u.Id).ToListAsync();
        foreach (var id in ids)
        {
            await cache.RemoveAsync(CacheKeys.UserPermissions(id), cancellationToken);
            await cache.RemoveAsync(CacheKeys.UserDataScope(id), cancellationToken);
        }
        return ids.Count;
    }

    /// <inheritdoc />
    public virtual async Task<int> FlushDictAsync(CancellationToken cancellationToken = default)
    {
        var codes = await dictTypes.AsQueryable().Select(t => t.Code).ToListAsync();
        foreach (var code in codes)
            await cache.RemoveAsync(CacheKeys.DictItems(code), cancellationToken);
        return codes.Count;
    }

    /// <inheritdoc />
    public virtual async Task<int> FlushConfigAsync(CancellationToken cancellationToken = default)
    {
        var keys = await configs.AsQueryable().Select(c => c.ConfigKey).ToListAsync();
        foreach (var key in keys)
            await cache.RemoveAsync(CacheKeys.Config(key), cancellationToken);
        return keys.Count;
    }

    /// <inheritdoc />
    public virtual Task<long> RebuildPortalAsync(CancellationToken cancellationToken = default) =>
        // 与 RbacService 授权变更时同一机制:自增代际,旧 portal:* 键不再被读到、由 TTL 回收(不设过期,同 CacheKeys 约定)
        cache.IncrementAsync(CacheKeys.PortalGeneration, cancellationToken: cancellationToken);
}
