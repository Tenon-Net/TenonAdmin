using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 认证端点(设计 §4 认证模块 / §15 会话模型)。标准 [ApiController]——可按模块禁用后由用户同路由接管(设计 §5.4)。
/// 验证码端点随验证码机制(T8)接入时增补。
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService auth, ICaptchaService captcha) : ControllerBase
{
    /// <summary>获取登录验证码(SVG)。匿名;返回票据 Id + SVG,登录时回传票据 Id + 输入码。</summary>
    [HttpGet("captcha")]
    [AllowAnonymous]
    public async Task<Result<CaptchaOutput>> Captcha() =>
        Result<CaptchaOutput>.Ok(await captcha.IssueAsync());

    /// <summary>账密登录。业务失败(密码错/停用)由 AdminExceptionFilter 转统一信封,这里只写成功路径。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> Login(LoginInput input) =>
        Result<LoginOutput>.Ok(await auth.LoginAsync(input));

    /// <summary>刷新令牌换发新令牌对(轮换 + 复用检测,§15)。匿名:访问令牌可能已过期,凭刷新令牌换发。</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> Refresh(RefreshInput input) =>
        Result<LoginOutput>.Ok(await auth.RefreshAsync(input));

    /// <summary>登出:吊销当前会话(sid 取自令牌)。仅需认证,不挂具体权限码。</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<Result<bool>> Logout()
    {
        var sessionId = User.FindFirstValue(TokenClaimNames.SESSION_ID);
        if (!string.IsNullOrEmpty(sessionId)) await auth.LogoutAsync(sessionId);
        return Result<bool>.Ok(true);
    }
}
