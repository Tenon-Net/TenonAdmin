using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using TenonAdmin.Core;

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

        // 1. 必须已通过 JWT 认证(令牌缺失/过期/被篡改在认证中间件即被拒)
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // 2. 超管直接放行(claim 随令牌下发,零查库;设计 §6 授权管道第一步)
        if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
            return;

        // 3. 普通用户:权限码 = 规范化路由(含 HTTP Method),与用户权限码集合比对
        var code = BuildPermissionCode(context);
        var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var provider = context.HttpContext.RequestServices.GetRequiredService<IPermissionProvider>();
        var codes = await provider.GetPermissionCodesAsync(userId, context.HttpContext.RequestAborted);

        if (!codes.Contains(code))
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.NoPermission))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
    }

    /// <summary>
    /// 规范化权限码:<c>{大写 Method}:/{小写路由模板}</c>。
    /// 用路由模板而非实际路径——带参数的路由(<c>user/{id}</c>)权限码稳定,不随参数值变化。
    /// </summary>
    private static string BuildPermissionCode(AuthorizationFilterContext context)
    {
        var template = context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.HttpContext.Request.Path.Value ?? "";
        return $"{context.HttpContext.Request.Method.ToUpperInvariant()}:/{template.TrimStart('/').ToLowerInvariant()}";
    }
}
