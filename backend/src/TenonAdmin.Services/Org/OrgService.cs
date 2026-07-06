using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IOrgService"/> 默认实现。机构树本身不做环检测(设计上只禁止"父指向自己"这一种一步环),
/// 更复杂的多级环由前端拼树时天然规避(找不到父节点的机构不会出现在树上)。
/// </summary>
public class OrgService(IRepository<SysOrg> orgs) : IOrgService
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<SysOrg>> ListAsync() =>
        await orgs.AsQueryable().OrderBy(o => o.Sort).OrderBy(o => o.Id).ToListAsync();

    /// <inheritdoc />
    public virtual async Task<SysOrg> GetAsync(long id)
    {
        var org = await orgs.GetByIdAsync(id);
        AdminException.ThrowIf(org is null, ErrorCode.OrgNotFound);
        return org!;
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(OrgInput input)
    {
        if (input.ParentId != 0)
            AdminException.ThrowIf(!await orgs.AnyAsync(o => o.Id == input.ParentId), ErrorCode.OrgNotFound);

        var entity = new SysOrg
        {
            ParentId = input.ParentId,
            Name = input.Name,
            Code = input.Code,
            Sort = input.Sort,
            Enabled = input.Enabled,
        };
        await orgs.InsertAsync(entity);
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, OrgInput input)
    {
        AdminException.ThrowIf(input.ParentId == id, ErrorCode.OrgNotFound);

        var entity = await GetAsync(id);
        entity.ParentId = input.ParentId;
        entity.Name = input.Name;
        entity.Code = input.Code;
        entity.Sort = input.Sort;
        entity.Enabled = input.Enabled;
        await orgs.UpdateAsync(entity);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        AdminException.ThrowIf(await orgs.AnyAsync(o => o.ParentId == id), ErrorCode.OrgHasChildren);
        await orgs.DeleteAsync(id);
    }
}
