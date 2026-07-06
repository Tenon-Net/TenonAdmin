using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 认证端点(设计 §4 认证模块)。标准 [ApiController]——可按模块禁用后由用户同路由接管(设计 §5.4)。
/// 刷新/登出/验证码端点随对应机制(§15 会话模块)接入时增补。
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>账密登录。业务失败(密码错/停用)由 AdminExceptionFilter 转统一信封,这里只写成功路径。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> Login(LoginInput input) =>
        Result<LoginOutput>.Ok(await auth.LoginAsync(input));
}
