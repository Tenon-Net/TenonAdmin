using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// <see cref="ICurrentUser"/> 默认实现:从当前请求的 JWT claim 读取(不查库)。
/// sub → 用户 Id;sadm → 超管标志(与授权管道判定一致)。
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public long? UserId =>
        long.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public bool IsSuperAdmin => Principal?.HasClaim(TokenClaimNames.SUPER_ADMIN, "true") == true;
}
