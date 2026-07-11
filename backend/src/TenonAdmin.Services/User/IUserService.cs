using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 用户管理服务(设计 §4 组织模块)——用户的增删改查分页 + 重置密码 + 启停用 + 角色分配。
/// <para>安全不变量(实现须守住):密码哈希绝不出接口;<c>IsSuperAdmin</c> 绝不经接口设置(防提权);
/// 超级管理员不可删除/停用(防自锁死)。类 public、方法 virtual 便于覆写(设计 §5.3)。</para>
/// </summary>
public interface IUserService
{
    /// <summary>分页查询(账号/姓名模糊 + 机构/状态过滤)</summary>
    Task<PagedList<UserItem>> PageAsync(UserPageInput input);

    /// <summary>取用户详情(含角色 Id 集合);不存在抛 <see cref="ErrorCode.UserNotFound"/></summary>
    Task<UserDetail> GetAsync(long id);

    /// <summary>新增用户(账号唯一;分配初始角色)。返回新用户 Id。</summary>
    Task<long> AddAsync(AddUserInput input);

    /// <summary>更新用户资料与角色(不改账号/密码/超管标志)</summary>
    Task UpdateAsync(long id, UpdateUserInput input);

    /// <summary>软删除用户(超管不可删)</summary>
    Task DeleteAsync(long id);

    /// <summary>批量软删除用户(集合内含超管则整体拒绝,守超管不可删不变量);不存在的 Id 静默跳过。</summary>
    Task DeleteBatchAsync(IReadOnlyCollection<long> ids);

    /// <summary>重置密码为指定值;留空则重置为默认初始密码。返回实际生效的初始密码明文(仅本次返回给管理员转达)。</summary>
    Task<string> ResetPasswordAsync(long id, string? newPassword);

    /// <summary>启用/停用(超管不可停用)</summary>
    Task SetEnabledAsync(long id, bool enabled);
}
