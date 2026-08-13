using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.AspNetCore;

/// <summary>回收站统一 DTO</summary>
public record RecycleBinItem(long Id, string Name, string? Code, DateTime? DeletedAt, long? DeletedBy);

/// <summary>回收站分页入参</summary>
public record RecycleBinPageInput : PageInputBase;

/// <summary>
/// 全局回收站——按实体类型查看软删除数据、恢复或彻底删除。
/// 路由 <c>/api/v1/sys/recycle/{type}/...</c>,type 值:user/role/org/position/module/config/dict/menu。
/// </summary>
[ApiController]
[Route("api/v1/sys/recycle")]
public class RecycleBinController(
    ISqlSugarClient db,
    IServiceProvider sp,
    IRbacService rbac,
    ICacheProvider cache) : ControllerBase
{
    /// <summary>分页列出指定类型的已删记录</summary>
    [HttpGet("{type}/page")]
    [RolePermission]
    public async Task<Result<PagedList<RecycleBinItem>>> Page(string type, [FromQuery] RecycleBinPageInput input)
    {
        var data = type switch
        {
            "user" => await ListAsync<SysUser>(input, e => e.Account + " / " + e.Name, null),
            "role" => await ListAsync<SysRole>(input, e => e.Name, e => e.Code),
            "org" => await ListAsync<SysOrg>(input, e => e.Name, e => e.Code),
            "position" => await ListAsync<SysPosition>(input, e => e.Name, e => e.Code),
            "module" => await ListAsync<SysModule>(input, e => e.Title, e => e.Code),
            "config" => await ListAsync<SysConfig>(input, e => e.Name, e => e.ConfigKey),
            "dict" => await ListAsync<SysDictType>(input, e => e.Name, e => e.Code),
            "menu" => await ListAsync<SysMenu>(input, e => e.Title, e => e.Permission),
            "job" => await ListAsync<SysJob>(input, e => e.Name, e => e.Code),
            _ => throw new AdminException(ErrorCode.RecycleInvalidType),
        };
        return Result<PagedList<RecycleBinItem>>.Ok(data);
    }

    /// <summary>恢复已删记录</summary>
    [HttpPost("{type}/{id}/restore")]
    [RolePermission]
    [OperationLog("回收站-恢复")]
    public async Task<Result<bool>> Restore(string type, long id)
    {
        var rows = type switch
        {
            "user" => await RestoreUserAsync(id),
            "role" => await RestoreRoleAsync(id),
            "org" => await Repo<SysOrg>().RestoreAsync(id),
            "position" => await Repo<SysPosition>().RestoreAsync(id),
            "module" => await Repo<SysModule>().RestoreAsync(id),
            "config" => await Repo<SysConfig>().RestoreAsync(id),
            "dict" => await Repo<SysDictType>().RestoreAsync(id),
            "menu" => await Repo<SysMenu>().RestoreAsync(id),
            "job" => await RestoreJobAsync(id),
            _ => throw new AdminException(ErrorCode.RecycleInvalidType),
        };
        AdminException.ThrowIf(rows == 0, ErrorCode.RecycleNotFound);
        return Result<bool>.Ok(true);
    }

    /// <summary>QA23: restore user → invalidate permission/scope caches so restored associations take effect.</summary>
    private async Task<int> RestoreUserAsync(long id)
    {
        var rows = await Repo<SysUser>().RestoreAsync(id);
        if (rows > 0)
            await cache.IncrementAsync(CacheKeys.PortalGeneration);
        return rows;
    }

    /// <summary>QA23: restore role → invalidate caches for users who hold this role.</summary>
    private async Task<int> RestoreRoleAsync(long id)
    {
        var rows = await Repo<SysRole>().RestoreAsync(id);
        if (rows > 0)
        {
            await rbac.InvalidateByRoleAsync(id);
            await cache.IncrementAsync(CacheKeys.PortalGeneration);
        }
        return rows;
    }

    /// <summary>
    /// 恢复定时任务:<b>强制置 Paused</b>(scheduling-ledger §13-3)。
    /// 恢复出来的行 NextRunTime 是删除时的过去时刻,直接放回 Ready 会被当成错过而立刻补跑/推进——
    /// 人工在任务页 enable 才重算复跑,是恢复动作该有的语义。
    /// </summary>
    private async Task<int> RestoreJobAsync(long id)
    {
        var rows = await Repo<SysJob>().RestoreAsync(id);
        if (rows > 0)
            await db.Updateable<SysJob>()
                .SetColumns(j => new SysJob { Status = JobStatus.Paused, NextRunTime = null })
                .Where(j => j.Id == id)
                .ExecuteCommandAsync();
        return rows;
    }

    /// <summary>彻底删除(物理删除,不可恢复)</summary>
    [HttpDelete("{type}/{id}")]
    [RolePermission]
    [OperationLog("回收站-彻底删除")]   // 不可逆硬删,必须留审计
    public async Task<Result<bool>> Purge(string type, long id)
    {
        var rows = type switch
        {
            "user" => await PurgeUserAsync(id),
            "role" => await PurgeRoleAsync(id),
            "org" => await Repo<SysOrg>().HardDeleteAsync(id),
            "position" => await Repo<SysPosition>().HardDeleteAsync(id),
            "module" => await Repo<SysModule>().HardDeleteAsync(id),
            "config" => await Repo<SysConfig>().HardDeleteAsync(id),
            "dict" => await Repo<SysDictType>().HardDeleteAsync(id),
            "menu" => await Repo<SysMenu>().HardDeleteAsync(id),
            "job" => await Repo<SysJob>().HardDeleteAsync(id),
            _ => throw new AdminException(ErrorCode.RecycleInvalidType),
        };
        AdminException.ThrowIf(rows == 0, ErrorCode.RecycleNotFound);
        return Result<bool>.Ok(true);
    }

    /// <summary>QA23: purge user → clean associations before hard-delete.</summary>
    private async Task<int> PurgeUserAsync(long id)
    {
        await db.Deleteable<SysUserRole>().Where(ur => ur.UserId == id).ExecuteCommandAsync();
        var externalBindings = sp.GetService<ISysUserExternalService>();
        if (externalBindings is not null) await externalBindings.UnbindAllAsync(id);
        return await Repo<SysUser>().HardDeleteAsync(id);
    }

    /// <summary>QA23: purge role → clean associations before hard-delete.</summary>
    private async Task<int> PurgeRoleAsync(long id)
    {
        await rbac.OnRoleDeletedAsync(id);
        return await Repo<SysRole>().HardDeleteAsync(id);
    }

    private IRepository<T> Repo<T>() where T : BaseEntity, new() =>
        sp.GetRequiredService<IRepository<T>>();

    private async Task<PagedList<RecycleBinItem>> ListAsync<T>(
        RecycleBinPageInput input,
        Func<T, string> nameSelector,
        Func<T, string?>? codeSelector) where T : BaseEntity, new()
    {
        var query = db.Queryable<T>().ClearFilter<ISoftDelete>()
            .Where(e => e.IsDelete == true)
            .OrderByDescending(e => e.UpdateTime);

        var paged = await query.ToPagedListAsync(input.Current, input.Size);

        return new PagedList<RecycleBinItem>
        {
            Current = paged.Current,
            Size = paged.Size,
            Total = paged.Total,
            Items = paged.Items.Select(e => new RecycleBinItem(
                e.Id,
                nameSelector(e),
                codeSelector?.Invoke(e),
                e.UpdateTime,
                e.UpdateUserId
            )).ToList(),
        };
    }
}
