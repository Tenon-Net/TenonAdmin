using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 会话活性校验(设计 §15「强退即时生效」)——认证 + 会话仍活跃,但<b>不要求具体权限码</b>。
/// <para>用于个人中心/登出这类"任何已登录用户可用"的端点:仅 <c>[Authorize]</c> 只校验 JWT 签名与有效期,
/// 会话被强退/登出后仍能被未过期的 access token 继续调用(最长到令牌自然过期);挂本特性后与
/// <see cref="RolePermissionAttribute"/> 同源每请求校验 <see cref="ISessionService.IsActiveAsync"/>,强退即 401(P2-1)。</para>
/// <para>同时写入数据范围(与 <see cref="RolePermissionAttribute"/> 同源 <c>DataScopeRequestBinder</c>):
/// 未写入时 <see cref="HttpContextDataScopeContext"/> 默认 Unrestricted,消费方在仅登录端点上查
/// <c>DataEntity</c> 会看到全部机构。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ActiveSessionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid)) { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }

        var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID);
        var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionService>();
        if (string.IsNullOrEmpty(sessionId) || !await sessions.IsActiveAsync(sessionId))
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid)) { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }

        await DataScopeRequestBinder.BindAsync(context.HttpContext, user, context.HttpContext.RequestAborted);
    }
}
