using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 个人中心端点(设计 §4,T8)。<c>[ActiveSession]</c>——任何已登录用户可用,<b>无需具体权限码</b>,
/// 但与受权限保护的端点同源每请求校验会话活性:被强退/登出后即 401(不再靠令牌自然过期,P2-1);
/// 一切操作限当前登录用户自己(userId 取自令牌,不接受任意 Id)。
/// </summary>
[ApiController]
[Route("api/v1/personal")]
[ActiveSession]
public class PersonalController(IPersonalService personal, IMenuService menu, ICurrentUser currentUser) : ControllerBase
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

    /// <summary>取自己可访问的应用/模块列表 + 默认应用(多应用门户;登录后拉取以决定进哪个应用)</summary>
    [HttpGet("modules")]
    public async Task<Result<MyModulesOutput>> GetModules() =>
        Result<MyModulesOutput>.Ok(await personal.GetMyModulesAsync(CurrentUserId));

    /// <summary>取自己在指定应用下的侧边栏菜单树(切换应用时重新拉取以重建动态路由)</summary>
    [HttpGet("menu")]
    public async Task<Result<IReadOnlyList<MenuNode>>> GetMenu([FromQuery] long moduleId) =>
        Result<IReadOnlyList<MenuNode>>.Ok(await menu.GetMyMenuTreeAsync(CurrentUserId, currentUser.IsSuperAdmin, moduleId));

    /// <summary>设自己的默认应用(须为可访问的应用,否则 42014/ModuleAccessDenied)</summary>
    [HttpPut("default-module")]
    [OperationLog("设置默认应用")]
    public async Task<Result<bool>> SetDefaultModule(SetDefaultModuleInput input)
    {
        await personal.SetDefaultModuleAsync(CurrentUserId, input);
        return Result<bool>.Ok(true);
    }
}
