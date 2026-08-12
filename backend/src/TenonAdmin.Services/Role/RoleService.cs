using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IRoleService"/> 默认实现——角色本体是标准字典式 CRUD(照职位范式);
/// 与职位唯一的不同是<b>删除要级联清理关联</b>(用户↔角色 / 角色↔菜单 / 角色数据范围),
/// 这段清理 + 缓存失效逻辑复用 <see cref="IRbacService.OnRoleDeletedAsync"/>(那里已握有关联仓储与缓存)。
/// <para>
/// QA36:角色<b>定义</b>(新增/更新/删除/批量删除)是超管专属操作,越过路由权限之外的强约束——
/// 即便某普通管理员被授予了对应路由权限码,也不能创建/修改/删除角色本体(只能被超管授予"可转授"角色去分配给他人)。
/// <c>currentUser</c> 尾随可选:未注入(消费者精简子类/手工构造的旧测试)时不加限制,行为与批次前一致。
/// </para>
/// </summary>
public class RoleService(IRepository<SysRole> roles, IRbacService rbac, ICurrentUser? currentUser = null) : IRoleService
{
    /// <summary>超管专属操作守卫;系统/未认证上下文(与 <see cref="IDataScopeContext"/> 同一约定)视为可信,不受限。</summary>
    protected virtual void EnsureSuperAdmin() =>
        AdminException.ThrowIf(
            currentUser is { IsAuthenticated: true, IsSuperAdmin: false },
            ErrorCode.SuperAdminRequired);

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
        EnsureSuperAdmin();   // QA36:角色定义超管专属
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
            IsDelegatable = input.IsDelegatable,
        };
        await roles.InsertAsync(role);
        return role.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, RoleInput input)
    {
        EnsureSuperAdmin();   // QA36:角色定义超管专属
        var role = await GetAsync(id);
        AdminException.ThrowIf(
            input.Code != role.Code &&
            await roles.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(r => r.Code == input.Code && r.Id != id),
            ErrorCode.RoleCodeExists);

        var enabledChanged = input.Enabled != role.Enabled;
        role.Name = input.Name;
        role.Code = input.Code;
        role.Sort = input.Sort;
        role.Enabled = input.Enabled;
        role.Remark = input.Remark;
        role.IsDelegatable = input.IsDelegatable;
        await roles.UpdateAsync(role);

        if (enabledChanged)
            await rbac.InvalidateByRoleAsync(id);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        EnsureSuperAdmin();   // QA36:角色定义超管专属
        await GetAsync(id);
        await roles.DeleteAsync(id);
        await rbac.OnRoleDeletedAsync(id);   // 级联清关联 + 失效受影响用户权限/数据范围缓存
    }

    /// <inheritdoc />
    public virtual async Task DeleteBatchAsync(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return;
        var result = await roles.Db.Ado.UseTranAsync(async () =>
        {
            foreach (var id in ids)
                await DeleteAsync(id);
        });
        if (!result.IsSuccess) throw result.ErrorException;
    }
}
