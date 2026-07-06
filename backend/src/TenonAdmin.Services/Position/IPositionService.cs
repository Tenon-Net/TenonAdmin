using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 职位服务——职位字典的标准 CRUD + 分页查询(设计同角色管理的增改删查形态)。
/// </summary>
public interface IPositionService
{
    /// <summary>分页查询职位,按名称模糊过滤,按排序升序返回。</summary>
    Task<PagedList<SysPosition>> PageAsync(PositionPageInput input);

    /// <summary>按 Id 取单条,不存在抛 <see cref="ErrorCode.PositionNotFound"/>。</summary>
    Task<SysPosition> GetAsync(long id);

    /// <summary>新增职位,返回新 Id。</summary>
    Task<long> AddAsync(PositionInput input);

    /// <summary>更新职位(不存在抛 <see cref="ErrorCode.PositionNotFound"/>)。</summary>
    Task UpdateAsync(long id, PositionInput input);

    /// <summary>删除职位(软删,不存在抛 <see cref="ErrorCode.PositionNotFound"/>)。</summary>
    Task DeleteAsync(long id);
}
