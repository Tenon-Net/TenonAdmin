using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IPositionService"/> 默认实现——职位是纯字典表,无关联维护,直接走仓储标准 CRUD。
/// </summary>
public class PositionService(
    IRepository<SysPosition> positions,
    // QA10: trailing optional param to check active users before delete (§5.3 replaceability)
    IRepository<SysUser>? userRepo = null) : IPositionService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<SysPosition>> PageAsync(PositionPageInput input) =>
        await positions.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Name), p => p.Name.Contains(input.Name!))
            // 客户端排序优先(安全白名单),否则按 Sort 默认排序
            .ToPagedListAsync(input, q => q.OrderBy(p => p.Sort));

    /// <inheritdoc />
    public virtual async Task<SysPosition> GetAsync(long id)
    {
        var position = await positions.GetByIdAsync(id);
        AdminException.ThrowIf(position is null, ErrorCode.PositionNotFound);
        return position!;
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(PositionInput input)
    {
        var code = string.IsNullOrWhiteSpace(input.Code)
            ? Guid.NewGuid().ToString("N")[..10]
            : input.Code;

        // 用户显式填了编码才做前置查重(纳入软删行);自动生成的靠 DB 唯一索引兜底
        if (!string.IsNullOrWhiteSpace(input.Code))
            AdminException.ThrowIf(
                await positions.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(p => p.Code == code),
                ErrorCode.PositionCodeExists);

        var position = new SysPosition
        {
            Name = input.Name,
            Code = code,
            Sort = input.Sort,
            Enabled = input.Enabled,
        };
        await positions.InsertAsync(position);
        return position.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, PositionInput input)
    {
        var position = await GetAsync(id);
        AdminException.ThrowIf(
            input.Code != position.Code &&
            await positions.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(p => p.Code == input.Code && p.Id != id),
            ErrorCode.PositionCodeExists);

        position.Name = input.Name;
        position.Code = input.Code;
        position.Sort = input.Sort;
        position.Enabled = input.Enabled;
        await positions.UpdateAsync(position);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        await GetAsync(id);
        // QA10: block delete if any non-deleted user still holds this position
        if (userRepo is not null)
            AdminException.ThrowIf(await userRepo.AnyAsync(u => u.PositionId == id), ErrorCode.PositionHasUsers);
        await positions.DeleteAsync(id);
    }

}
