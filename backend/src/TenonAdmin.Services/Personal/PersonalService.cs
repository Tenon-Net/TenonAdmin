using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IPersonalService"/> 默认实现。改密码走"验旧 → 换新",与管理员 <c>ResetPassword</c>(不验旧、直接重置)
/// 刻意区分:自助改密必须证明知道当前密码,管理员重置面向找回场景。
/// </summary>
public class PersonalService(
    IRepository<SysUser> users,
    IPasswordHasher hasher,
    ISecurityPolicyProvider policy,
    IMenuService menu) : IPersonalService
{
    /// <inheritdoc />
    public virtual async Task<UserProfile> GetProfileAsync(long userId)
    {
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        return new UserProfile
        {
            Id = user!.Id,
            Account = user.Account,
            Name = user.Name,
            OrgId = user.OrgId,
            PositionId = user.PositionId,
            IsSuperAdmin = user.IsSuperAdmin,
        };
    }

    /// <inheritdoc />
    public virtual async Task UpdateProfileAsync(long userId, UpdateProfileInput input)
    {
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        user!.Name = input.Name;   // 只改姓名;账号/机构/职位/超管标志一律不动
        await users.UpdateAsync(user);
    }

    /// <inheritdoc />
    public virtual async Task ChangePasswordAsync(long userId, ChangePasswordInput input)
    {
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        // 验旧密码:错就按"密码错误"拒(与登录同码,不泄漏更多信息)
        AdminException.ThrowIf(!hasher.Verify(input.OldPassword, user!.Password), ErrorCode.PasswordWrong);

        await policy.ValidatePasswordAsync(input.NewPassword);   // 新口令须满足密码复杂度策略(运行时可配)
        user.Password = hasher.Hash(input.NewPassword);
        await users.UpdateAsync(user);
    }

    /// <inheritdoc />
    public virtual async Task<MyModulesOutput> GetMyModulesAsync(long userId)
    {
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        var modules = await menu.GetMyModulesAsync(userId, user!.IsSuperAdmin);
        return new MyModulesOutput { Modules = modules, DefaultModuleId = user.DefaultModuleId };
    }

    /// <inheritdoc />
    public virtual async Task SetDefaultModuleAsync(long userId, SetDefaultModuleInput input)
    {
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        // 只能把默认设为自己有权访问的模块(GetMyModulesAsync 已含"存在+启用+有权"三重过滤;超管得全部)
        var modules = await menu.GetMyModulesAsync(userId, user!.IsSuperAdmin);
        AdminException.ThrowIf(modules.All(m => m.Id != input.ModuleId), ErrorCode.ModuleAccessDenied);

        user.DefaultModuleId = input.ModuleId;
        await users.UpdateAsync(user);
    }
}
