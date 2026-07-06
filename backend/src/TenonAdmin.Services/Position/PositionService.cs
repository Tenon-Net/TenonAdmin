using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IPositionService"/> 默认实现——职位是纯字典表,无关联维护,直接走仓储标准 CRUD。
/// </summary>
public class PositionService(IRepository<SysPosition> positions) : IPositionService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<SysPosition>> PageAsync(PositionPageInput input) =>
        await positions.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Name), p => p.Name.Contains(input.Name!))
            .OrderBy(p => p.Sort)
            .ToPagedListAsync(input.Current, input.Size);

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
        var position = new SysPosition
        {
            Name = input.Name,
            Code = input.Code,
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
        await positions.DeleteAsync(id);
    }
}
