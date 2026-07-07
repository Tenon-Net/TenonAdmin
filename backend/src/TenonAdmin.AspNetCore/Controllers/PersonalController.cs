using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 个人中心端点(设计 §4,T8)。<c>[Authorize]</c>——任何已登录用户可用,<b>无需具体权限码</b>
/// (与登出一致);一切操作限当前登录用户自己(userId 取自令牌,不接受任意 Id)。
/// </summary>
[ApiController]
[Route("api/v1/personal")]
[Authorize]
public class PersonalController(IPersonalService personal, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>当前用户 Id;[Authorize] 已保证认证,理论上不为空,兜底当令牌异常处理。</summary>
    private long CurrentUserId => currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);

    /// <summary>看自己的资料</summary>
    [HttpGet("profile")]
    public async Task<Result<UserProfile>> GetProfile() =>
        Result<UserProfile>.Ok(await personal.GetProfileAsync(CurrentUserId));

    /// <summary>改自己的资料(姓名)</summary>
    [HttpPut("profile")]
    [OperationLog("修改个人资料")]
    public async Task<Result<bool>> UpdateProfile(UpdateProfileInput input)
    {
        await personal.UpdateProfileAsync(CurrentUserId, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>改自己的密码(验旧密码;OldPassword/NewPassword 在操作日志里会被脱敏)</summary>
    [HttpPut("password")]
    [OperationLog("修改密码")]
    public async Task<Result<bool>> ChangePassword(ChangePasswordInput input)
    {
        await personal.ChangePasswordAsync(CurrentUserId, input);
        return Result<bool>.Ok(true);
    }
}
