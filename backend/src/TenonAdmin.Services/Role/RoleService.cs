using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IRoleService"/> 默认实现——角色本体是标准字典式 CRUD(照职位范式);
/// 与职位唯一的不同是<b>删除要级联清理关联</b>(用户↔角色 / 角色↔菜单 / 角色数据范围),
/// 这段清理 + 缓存失效逻辑复用 <see cref="IRbacService.OnRoleDeletedAsync"/>(那里已握有关联仓储与缓存)。
/// </summary>
public class RoleService(IRepository<SysRole> roles, IRbacService rbac) : IRoleService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<SysRole>> PageAsync(RolePageInput input) =>
        await roles.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Name), r => r.Name.Contains(input.Name!))
            .OrderBy(r => r.Sort)
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    public virtual async Task<SysRole> GetAsync(long id)
    {
        var role = await roles.GetByIdAsync(id);
        AdminException.ThrowIf(role is null, ErrorCode.RoleNotFound);
        return role!;
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(RoleInput input)
    {
        // 编码唯一(库有 idx_sys_role_code 唯一索引):查重纳入软删行,避免撞约束抛原生 500(同职位 P1-10)
        AdminException.ThrowIf(
            await roles.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(r => r.Code == input.Code),
            ErrorCode.RoleCodeExists);

        var role = new SysRole
        {
            Name = input.Name,
            Code = input.Code,
            Sort = input.Sort,
            Enabled = input.Enabled,
            Remark = input.Remark,
        };
        await roles.InsertAsync(role);
        return role.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, RoleInput input)
    {
        var role = await GetAsync(id);
        AdminException.ThrowIf(
            input.Code != role.Code &&
            await roles.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(r => r.Code == input.Code && r.Id != id),
            ErrorCode.RoleCodeExists);

        role.Name = input.Name;
        role.Code = input.Code;
        role.Sort = input.Sort;
        role.Enabled = input.Enabled;
        role.Remark = input.Remark;
        await roles.UpdateAsync(role);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        await GetAsync(id);
        await roles.DeleteAsync(id);
        await rbac.OnRoleDeletedAsync(id);   // 级联清关联 + 失效受影响用户权限/数据范围缓存
    }

    /// <inheritdoc />
    public virtual async Task DeleteBatchAsync(IReadOnlyCollection<long> ids)
    {
        foreach (var id in ids)
            await DeleteAsync(id);
    }
}
