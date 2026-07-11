using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 角色服务——角色的标准 CRUD + 分页查询(角色的授权/数据范围由 <see cref="IRbacService"/> 承载,不在此)。
/// <para>类 public、方法 virtual:接外部角色中心或加自定义规则时继承覆写(设计 §5.3)。</para>
/// </summary>
public interface IRoleService
{
    /// <summary>分页查询角色,按名称模糊过滤,按排序升序返回。</summary>
    Task<PagedList<SysRole>> PageAsync(RolePageInput input);

    /// <summary>按 Id 取单条,不存在抛 <see cref="ErrorCode.RoleNotFound"/>。</summary>
    Task<SysRole> GetAsync(long id);

    /// <summary>新增角色,返回新 Id。编码唯一,重复抛 <see cref="ErrorCode.RoleCodeExists"/>。</summary>
    Task<long> AddAsync(RoleInput input);

    /// <summary>更新角色(不存在抛 <see cref="ErrorCode.RoleNotFound"/>,改编码撞已有抛 <see cref="ErrorCode.RoleCodeExists"/>)。</summary>
    Task UpdateAsync(long id, RoleInput input);

    /// <summary>删除角色(软删本体 + 级联清理"用户↔角色/角色↔菜单/角色数据范围"关联并失效受影响用户缓存)。</summary>
    Task DeleteAsync(long id);

    /// <summary>批量删除角色(逐个复用 <see cref="DeleteAsync"/> 的级联;空集合视为无操作)。</summary>
    Task DeleteBatchAsync(IReadOnlyCollection<long> ids);
}
