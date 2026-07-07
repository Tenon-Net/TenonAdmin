using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 个人中心(设计 §4,T8)——当前登录用户对<b>自己</b>账号的读改:看资料、改资料、改密码。
/// 一切以传入的 <c>userId</c>(取自令牌)为准,不接受任意 Id,天然只能操作自己。
/// </summary>
public interface IPersonalService
{
    /// <summary>取当前用户资料;用户不存在抛 <see cref="ErrorCode.UserNotFound"/>。</summary>
    Task<UserProfile> GetProfileAsync(long userId);

    /// <summary>改当前用户资料(姓名);用户不存在抛 <see cref="ErrorCode.UserNotFound"/>。</summary>
    Task UpdateProfileAsync(long userId, UpdateProfileInput input);

    /// <summary>改当前用户密码:先验旧密码(错则抛 <see cref="ErrorCode.PasswordWrong"/>),再设新密码。</summary>
    Task ChangePasswordAsync(long userId, ChangePasswordInput input);
}
