using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 演示模式全局过滤器:仅放行 GET/HEAD/OPTIONS 和认证相关写接口,其余一律 403。
/// 仅在 <see cref="TenonAdminOptions.DemoMode"/> 为 true 时注册。
/// </summary>
public class DemoModeFilter : IAuthorizationFilter
{
    private static readonly HashSet<string> s_readMethods = new(StringComparer.OrdinalIgnoreCase)
        { "GET", "HEAD", "OPTIONS" };

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (s_readMethods.Contains(context.HttpContext.Request.Method))
            return;

        // 认证端点(登录/登出/刷新)必须放行,否则无法使用系统
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase))
            return;

        context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.DemoModeReadOnly))
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
