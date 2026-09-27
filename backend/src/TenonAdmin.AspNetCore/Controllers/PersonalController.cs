using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 个人中心端点。<c>[ActiveSession]</c> 校验会话活性,无需具体权限码;
/// 所有操作限当前用户,userId 取自令牌。
/// </summary>
[ApiController]
[Route("api/v1/personal")]
[ActiveSession]
public class PersonalController(IPersonalService personal, IMenuService menu, IPermissionProvider permissions, ICurrentUser currentUser, ISessionService sessions) : ControllerBase
{
    /// <summary>当前用户 Id;[Authorize] 已保证认证,理论上不为空,兜底当令牌异常处理。</summary>
    private long CurrentUserId => currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);

    /// <summary>看自己的资料</summary>
    [HttpGet("profile")]
    public async Task<Result<UserProfile>> GetProfile() =>
        Result<UserProfile>.Ok(await personal.GetProfileAsync(CurrentUserId));

    /// <summary>修改自己的资料。</summary>
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

    /// <summary>
    /// 取自己当前生效的权限码集合(= 规范化路由,如 <c>POST:/api/v1/sys/user</c>)。
    /// 空集不代表超管;前端须结合独立的 IsSuperAdmin 标志判断按钮显隐,服务端仍独立鉴权。
    /// </summary>
    [HttpGet("permissions")]
    public async Task<Result<IReadOnlyCollection<string>>> GetPermissions() =>
        Result<IReadOnlyCollection<string>>.Ok(await permissions.GetPermissionCodesAsync(CurrentUserId));

    /// <summary>取自己可访问的应用/模块列表 + 默认应用(多应用门户;登录后拉取以决定进哪个应用)</summary>
    [HttpGet("modules")]
    public async Task<Result<MyModulesOutput>> GetModules() =>
        Result<MyModulesOutput>.Ok(await personal.GetMyModulesAsync(CurrentUserId));

    /// <summary>取自己在指定应用下的侧边栏菜单树(切换应用时重新拉取以重建动态路由)</summary>
    [HttpGet("menu")]
    public async Task<Result<IReadOnlyList<MenuNode>>> GetMenu([FromQuery] long moduleId) =>
        Result<IReadOnlyList<MenuNode>>.Ok(await menu.GetMyMenuTreeAsync(CurrentUserId, currentUser.IsSuperAdmin, moduleId));

    /// <summary>
    /// 查看自己的活跃会话;IsCurrent 按令牌 sid 标记本次请求所用会话。
    /// </summary>
    [HttpGet("sessions")]
    public async Task<Result<IReadOnlyList<MySessionItem>>> GetSessions()
    {
        var mine = await sessions.ListOnlineAsync(new SessionPageInput { UserId = CurrentUserId, Current = 1, Size = 100 });
        var currentSid = User.FindFirstValue(TokenClaimNames.SESSION_ID);
        IReadOnlyList<MySessionItem> items = [.. mine.Items.Select(s => new MySessionItem
        {
            SessionId = s.SessionId,
            Ip = s.Ip,
            UserAgent = s.UserAgent,
            LoginTime = s.LoginTime,
            ExpiresAt = s.ExpiresAt,
            IsCurrent = s.SessionId == currentSid,
        })];
        return Result<IReadOnlyList<MySessionItem>>.Ok(items);
    }

    /// <summary>
    /// 下线自己的某个会话(自助踢设备)。只能踢自己名下的活跃会话:先按 UserId=自己 校验归属,
    /// 不命中一律 42024——"存在但不是你的"与"不存在"不区分,防探测他人会话。
    /// </summary>
    [HttpDelete("sessions/{sessionId}")]
    [OperationLog("下线我的会话")]
    public async Task<Result<bool>> RevokeSession(string sessionId)
    {
        var mine = await sessions.ListOnlineAsync(new SessionPageInput { UserId = CurrentUserId, Current = 1, Size = 100 });
        AdminException.ThrowIf(mine.Items.All(s => s.SessionId != sessionId), ErrorCode.SessionNotFound);
        await sessions.RevokeAsync(sessionId);
        return Result<bool>.Ok(true);
    }

    /// <summary>设自己的默认应用(须为可访问的应用,否则 42014/ModuleAccessDenied)</summary>
    [HttpPut("default-module")]
    [OperationLog("设置默认应用")]
    public async Task<Result<bool>> SetDefaultModule(SetDefaultModuleInput input)
    {
        await personal.SetDefaultModuleAsync(CurrentUserId, input);
        return Result<bool>.Ok(true);
    }
}
