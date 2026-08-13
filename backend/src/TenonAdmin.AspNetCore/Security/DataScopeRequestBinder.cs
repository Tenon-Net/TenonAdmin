using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 把当前登录用户的生效数据范围写入 <see cref="IDataScopeContext"/>。
/// <para><see cref="RolePermissionAttribute"/> 与 <see cref="ActiveSessionAttribute"/> 共用,
/// 避免仅登录端点漏绑后落到 <see cref="DataScopeResult.Unrestricted"/> 默认(业务表 IOrgScoped 查询会看全机构)。</para>
/// </summary>
internal static class DataScopeRequestBinder
{
    public static async Task BindAsync(HttpContext http, ClaimsPrincipal user, CancellationToken abort)
    {
        var services = http.RequestServices;
        var scopeContext = services.GetRequiredService<IDataScopeContext>();

        if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
        {
            scopeContext.Current = DataScopeResult.Unrestricted;
            return;
        }

        var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        scopeContext.Current = await services.GetRequiredService<IDataScopeProvider>().ResolveAsync(userId, abort);
    }
}
