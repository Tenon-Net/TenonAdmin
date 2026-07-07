using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IPersonalService"/> 默认实现。改密码走"验旧 → 换新",与管理员 <c>ResetPassword</c>(不验旧、直接重置)
/// 刻意区分:自助改密必须证明知道当前密码,管理员重置面向找回场景。
/// </summary>
public class PersonalService(
    IRepository<SysUser> users,
    IPasswordHasher hasher) : IPersonalService
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

        // ponytail: 新密码强度校验待 T8 密码策略子轮接入(PasswordPolicy:MinLength 等);此处只落新哈希。
        user.Password = hasher.Hash(input.NewPassword);
        await users.UpdateAsync(user);
    }
}
