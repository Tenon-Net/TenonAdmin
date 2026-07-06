using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 会话管理端点(设计 §15):在线用户列表 + 强制下线。全部 <c>[RolePermission]</c>(超管放行,普通用户需授码)。
/// </summary>
[ApiController]
[Route("api/v1/sys/session")]
public class SysSessionController(ISessionService sessions) : ControllerBase
{
    /// <summary>在线会话分页(活跃且未过期)</summary>
    [HttpGet("online")]
    [RolePermission]
    public async Task<Result<PagedList<OnlineSessionItem>>> Online([FromQuery] SessionPageInput input) =>
        Result<PagedList<OnlineSessionItem>>.Ok(await sessions.ListOnlineAsync(input));

    /// <summary>强制下线指定会话(强退);该会话原访问令牌下次请求即 401。</summary>
    [HttpDelete("{sessionId}")]
    [RolePermission]
    public async Task<Result<bool>> ForceLogout(string sessionId)
    {
        await sessions.RevokeAsync(sessionId);
        return Result<bool>.Ok(true);
    }
}
