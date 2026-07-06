using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IUserService"/> 默认实现。角色分配复用 <see cref="IRbacService"/>(单一出口,顺带失效权限缓存)。
/// 守住三条安全不变量:密码哈希不出接口(出参用不含 Password 的 <see cref="UserItem"/>)、
/// 接口不建/不改超管标志(<c>IsSuperAdmin</c> 恒 false,不从入参取)、超管不可删/停(防自锁死)。
/// </summary>
public class UserService(
    IRepository<SysUser> users,
    IPasswordHasher hasher,
    IRbacService rbac) : IUserService
{
    /// <summary>
    /// 默认初始密码。ponytail: 固定常量 + T8 接入密码策略后改为"可配置默认 + 首次登录强制改密"。
    /// 管理员新建/重置密码后应告知用户尽快自行修改。
    /// </summary>
    private const string DEFAULT_PASSWORD = "Tenon@123456";

    /// <inheritdoc />
    public virtual Task<PagedList<UserItem>> PageAsync(UserPageInput input) =>
        users.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Account), u => u.Account.Contains(input.Account!))
            .WhereIF(!string.IsNullOrEmpty(input.Name), u => u.Name.Contains(input.Name!))
            .WhereIF(input.OrgId.HasValue, u => u.OrgId == input.OrgId)
            .WhereIF(input.Enabled.HasValue, u => u.Enabled == input.Enabled!.Value)
            .OrderBy(u => u.Id)
            // 投影到 UserItem:SQL 层就不取 Password 列,哈希从不进内存/出接口
            .Select(u => new UserItem
            {
                Id = u.Id,
                Account = u.Account,
                Name = u.Name,
                OrgId = u.OrgId,
                PositionId = u.PositionId,
                Enabled = u.Enabled,
                IsSuperAdmin = u.IsSuperAdmin,
                CreateTime = u.CreateTime,
            })
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    public virtual async Task<UserDetail> GetAsync(long id)
    {
        var u = await users.GetByIdAsync(id);
        AdminException.ThrowIf(u is null, ErrorCode.UserNotFound);
        var roleIds = await rbac.GetUserRoleIdsAsync(id);
        return new UserDetail
        {
            Id = u!.Id,
            Account = u.Account,
            Name = u.Name,
            OrgId = u.OrgId,
            PositionId = u.PositionId,
            Enabled = u.Enabled,
            IsSuperAdmin = u.IsSuperAdmin,
            CreateTime = u.CreateTime,
            RoleIds = roleIds,
        };
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(AddUserInput input)
    {
        AdminException.ThrowIf(await users.AnyAsync(u => u.Account == input.Account), ErrorCode.AccountExists);

        var password = string.IsNullOrEmpty(input.Password) ? DEFAULT_PASSWORD : input.Password;
        var user = new SysUser
        {
            Account = input.Account,
            Password = hasher.Hash(password),
            Name = input.Name,
            OrgId = input.OrgId,
            PositionId = input.PositionId,
            Enabled = input.Enabled,
            IsSuperAdmin = false,       // 接口永不建超管(防提权);超管只能种子/手工建
        };
        await users.InsertAsync(user);  // 插入后 AOP 已把雪花 Id 回填到 user.Id

        if (input.RoleIds.Count > 0) await rbac.SetUserRolesAsync(user.Id, input.RoleIds);
        return user.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, UpdateUserInput input)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        // 只改资料字段;Account/Password/IsSuperAdmin 原样保留(整行更新时未改动即不变)
        user!.Name = input.Name;
        user.OrgId = input.OrgId;
        user.PositionId = input.PositionId;
        user.Enabled = input.Enabled;
        await users.UpdateAsync(user);

        await rbac.SetUserRolesAsync(id, input.RoleIds);   // 全量重设角色 + 失效其权限缓存
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(user!.IsSuperAdmin, ErrorCode.SuperAdminProtected);

        await rbac.SetUserRolesAsync(id, []);   // 先清角色(顺带失效缓存),再软删——避免残留孤儿关联
        await users.DeleteAsync(id);
    }

    /// <inheritdoc />
    public virtual async Task<string> ResetPasswordAsync(long id, string? newPassword)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        var password = string.IsNullOrEmpty(newPassword) ? DEFAULT_PASSWORD : newPassword;
        user!.Password = hasher.Hash(password);
        await users.UpdateAsync(user);
        return password;   // 返回明文仅供管理员当场转达;不落日志(调用方注意脱敏)
    }

    /// <inheritdoc />
    public virtual async Task SetEnabledAsync(long id, bool enabled)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(!enabled && user!.IsSuperAdmin, ErrorCode.SuperAdminProtected);

        user!.Enabled = enabled;
        await users.UpdateAsync(user);
    }
}
