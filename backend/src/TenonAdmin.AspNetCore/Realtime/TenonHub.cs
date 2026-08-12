using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 实时推送 Hub(§14 实时通知)。<b>纯服务端→客户端推送</b>,不定义任何客户端可调方法(推送即信号,负载让客户端回查)。
/// <para>连接建立时按 claims 把连接归入两组:<c>user-{sub}</c>(按用户推,如公告)与 <c>session-{sid}</c>
/// (按会话精确推,force-logout 只踢被吊销的那次登录,不误伤同一用户的其他在线会话)。断开时 SignalR 自动清理组成员。</para>
/// <para>鉴权:<see cref="AuthorizeAttribute"/> + JwtBearer;浏览器 WebSocket 带不了 Authorization 头,令牌走 query
/// <c>access_token</c>(见 <c>TenonAdminSetup</c> 的 <c>OnMessageReceived</c>,仅在 Hub 路径采信)。</para>
/// <para>
/// QA25.1:JWT 通过期校验不代表会话仍活跃——强退/登出后原令牌在过期前仍是"合法令牌"。故连接建立时额外核验
/// <c>sid</c> 是否仍活跃(<see cref="ISessionService.IsActiveAsync"/>,与 <c>[RolePermission]</c> 过滤器同一判定源);
/// 缺失或已失效直接 <see cref="HubCallerContext.Abort"/>,不入任何组——避免被强退的连接仍能收到定向推送。
/// </para>
/// </summary>
[Authorize]
public sealed class TenonHub(ISessionService sessions) : Hub
{
    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var sub = user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var sid = user?.FindFirst(TokenClaimNames.SESSION_ID)?.Value;

        if (string.IsNullOrEmpty(sid) || !await sessions.IsActiveAsync(sid))
        {
            Context.Abort();
            return;
        }

        if (!string.IsNullOrEmpty(sub))
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.User(sub));
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Session(sid));
        await base.OnConnectedAsync();
    }
}
