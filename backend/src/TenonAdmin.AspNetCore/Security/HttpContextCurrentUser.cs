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

    // ponytail: 直接取 TCP 连接对端 IP。反向代理后面拿到的是代理 IP——上正式网关时按需接
    //           ForwardedHeaders 中间件解析 X-Forwarded-For(§14),这里不预埋。
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        // Headers.UserAgent 是强类型访问器(StringValues);空串归一为 null,日志里少一列噪声
        get
        {
            var ua = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrEmpty(ua) ? null : ua;
        }
    }
}
