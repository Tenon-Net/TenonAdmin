using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 接口授权标记(设计 §6,沿用旧版模型):<b>无参数、无权限字符串</b>——权限码就是规范化路由。
/// <para>授权管道:未认证 → 401;超管(令牌 sadm claim)→ 放行;
/// 其余取 <see cref="IPermissionProvider"/> 的权限码集合,包含
/// <c>{METHOD}:/{路由模板}</c>(如 <c>GET:/api/v1/ping</c>)才放行,否则 403 + 41001。</para>
/// <para>用户业务接口同样只需挂 <c>[RolePermission]</c>,角色-菜单授权界面上勾选路由即完成配权,
/// 代码里永远不出现 <c>"sys:user:add"</c> 之类的魔法字符串。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RolePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        var services = context.HttpContext.RequestServices;
        var abort = context.HttpContext.RequestAborted;

        // 1. 必须已通过 JWT 认证(令牌缺失/过期/被篡改在认证中间件即被拒)。
        //    401 也套统一信封(40006),与会话失活/框架 challenge 出口一致,前端可读 msgKey(P1-2)。
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid)) { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }

        // 2. 会话状态校验(§15 强退即时生效):sid 对应会话被吊销/过期 → 401,超管同样受此约束。
        var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID);
        if (string.IsNullOrEmpty(sessionId) || !await services.GetRequiredService<ISessionService>().IsActiveAsync(sessionId))
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid)) { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }

        // 数据范围载体:本请求后续的 DataEntity 查询由全局过滤器读它(设计 §6)。
        // 在授权阶段(动作执行前)写入,保证查询时已就绪。与 [ActiveSession] 同源,见 DataScopeRequestBinder。
        await DataScopeRequestBinder.BindAsync(context.HttpContext, user, abort);

        // 3. 超管直接放行(claim 随令牌下发,零查库;设计 §6 授权管道第一步)。范围已在 Bind 里写成 Unrestricted。
        if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
            return;

        var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        // 5. 权限码 = 规范化路由(含 HTTP Method),与用户权限码集合比对
        var code = BuildPermissionCode(context);
        var codes = await services.GetRequiredService<IPermissionProvider>().GetPermissionCodesAsync(userId, abort);

        if (!codes.Contains(code))
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.NoPermission))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
    }

    /// <summary>本请求对应的权限码(规范化规则见 <see cref="PermissionCode"/>——授权、路由清单、日志三处共用同一真源)。</summary>
    private static string BuildPermissionCode(AuthorizationFilterContext context) =>
        PermissionCode.Build(
            context.HttpContext.Request.Method,
            context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.HttpContext.Request.Path.Value);
}
