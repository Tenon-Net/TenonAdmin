using Microsoft.AspNetCore.Http;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// Level3 双提交 CSRF 中间件:当请求携带 refresh Cookie 时,
/// 对状态改变方法(POST/PUT/PATCH/DELETE)强制校验 <c>X-Tenon-CSRF</c> 与 <c>tenon_csrf</c> Cookie。
/// 非 Level3 / 纯 Bearer(无 refresh Cookie)直通,零行为变化。
/// </summary>
internal sealed class CsrfMiddleware(RequestDelegate next, AuthCookieService cookies)
{
    private static readonly HashSet<string> StateChanging = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete,
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!cookies.IsCookieSessionEnabled
            || !StateChanging.Contains(context.Request.Method)
            || !cookies.HasRefreshCookie(context))
        {
            await next(context);
            return;
        }

        // 登录端点本身在尚无 refresh Cookie 时不会进入此分支;
        // 刷新/登出/业务写操作在已有 Cookie 会话时必须带 CSRF。
        if (!cookies.ValidateCsrf(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.CsrfInvalid));
            return;
        }

        await next(context);
    }
}
