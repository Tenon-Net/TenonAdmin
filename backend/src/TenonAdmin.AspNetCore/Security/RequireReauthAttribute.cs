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
/// 高风险操作短时再次认证校验(约 5 分钟窗口)。
/// 按 JWT <c>sid</c> + userId 调用 <see cref="IReauthService.IsGrantedAsync"/>;
/// 未授予 → 403 + <see cref="ErrorCode.ReauthRequired"/>。
/// 不在 AuthService 内硬编码路由列表——由控制器/动作显式挂本特性。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireReauthAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 仅 TOTP 能力开启时强制再认证(SysConfig / Options / 历史 Level3)
        var policy = context.HttpContext.RequestServices.GetService<IMfaPolicyService>();
        if (policy is null || !await policy.IsTotpFeatureEnabledAsync())
            return;

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid))
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
            return;
        }

        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub) || !long.TryParse(sub, out var userId))
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid))
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
            return;
        }

        var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID)
                        ?? user.FindFirstValue("sid");

        var reauth = context.HttpContext.RequestServices.GetRequiredService<IReauthService>();
        if (!await reauth.IsGrantedAsync(userId, sessionId: sessionId))
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.ReauthRequired))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}
