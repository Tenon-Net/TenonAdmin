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
    IMenuService menu,
    IPasswordHistoryService? passwordHistory = null,
    // 统一时间源(§1.11):尾随可选参数,DI 正常注入;消费者子类省略也能编译(§5.3,同 FileGcService 成法)
    TimeProvider? time = null,
    // QA04:自助改密后吊销"其它"会话(保留当前会话,使本次响应正常返回);尾随可选,消费者子类省略也能编译
    ISessionService? sessions = null,
    ICurrentUser? currentUser = null,
    // QA25.3:头像 URL 校验(默认只放行 null/空白或本地签名直链);未注入(纯 Services 宿主)时跳过校验
    IAvatarUrlValidator? avatarValidator = null) : IPersonalService
{
    // LastPasswordChangeTime 是与审计字段同类的持久化业务时间戳,走本地时钟(与 SqlSugarSetup 的 GetLocalNow 审计口径一致)
    private DateTime Now => (time ?? TimeProvider.System).GetLocalNow().DateTime;

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
            OrgName = await OrgNameAsync(user.OrgId),
            PositionName = await PositionNameAsync(user.PositionId),
            Nickname = user.Nickname,
            Phone = user.Phone,
            Email = user.Email,
            Gender = user.Gender,
            Avatar = user.Avatar,
            IsSuperAdmin = user.IsSuperAdmin,
        };
    }

    /// <summary>
    /// 取机构名称(未分配/已删则 null)。
    /// <para>走 <c>users.Db</c> 逃生舱而非给主构造器加 <c>IRepository&lt;SysOrg&gt;</c>:加构造参数对<b>继承本类的消费者</b>
    /// 是源码破坏性变更(替换性契约由 ReplaceabilityTests 守)。单用户查单行,不必抄 <c>UserService</c> 那套批量防 N+1。</para>
    /// <para><c>SysOrg</c> 继承 <c>BaseEntity</c>(非 <c>DataEntity</c>),不带 <c>IOrgScoped</c> → 不受数据范围过滤器影响,
    /// 数据范围再窄的用户也能读到自己机构的名字。</para>
    /// </summary>
    protected virtual async Task<string?> OrgNameAsync(long? orgId) =>
        orgId is null ? null : await users.Db.Queryable<SysOrg>().Where(o => o.Id == orgId.Value).Select(o => o.Name).FirstAsync();

    /// <summary>取职位名称(未分配/已删则 null)。同上,<c>SysPosition</c> 亦非机构范围实体。</summary>
    protected virtual async Task<string?> PositionNameAsync(long? positionId) =>
        positionId is null ? null : await users.Db.Queryable<SysPosition>().Where(p => p.Id == positionId.Value).Select(p => p.Name).FirstAsync();

    /// <inheritdoc />
    public virtual async Task UpdateProfileAsync(long userId, UpdateProfileInput input)
    {
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(
            avatarValidator is not null && !avatarValidator.IsValid(input.Avatar),
            ErrorCode.AvatarUrlInvalid);
        // 只改自己能改的字段;账号/机构/职位/超管标志一律不动(全量替换语义,见 UpdateProfileInput)
        user!.Name = input.Name;
        user.Nickname = input.Nickname;
        user.Phone = input.Phone;
        user.Email = input.Email;
        user.Gender = input.Gender;
        user.Avatar = input.Avatar;
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
        await (passwordHistory?.EnsureNotReusedAsync(userId, input.NewPassword) ?? Task.CompletedTask);   // 防重用(策略开时;关/未注入时空操作)
        user.Password = hasher.Hash(input.NewPassword);
        user.MustChangePassword = false;   // 自助改密成功即清除强制改密标志(§14)
        user.LastPasswordChangeTime = Now;   // 密码过期窗口从本次改密重新起算
        await users.UpdateAsync(user);
        await (passwordHistory?.AppendAsync(userId, user.Password) ?? Task.CompletedTask);   // 记录新口令哈希,供后续防重用

        // 改密后吊销该用户的其它会话,只保留当前会话(本次响应仍能正常返回);
        // 旧会话对应的前端下次请求即 401,据此自愿登出重新登录。
        if (sessions is not null)
            await sessions.RevokeAllForUserExceptAsync(userId, currentUser?.SessionId);
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
