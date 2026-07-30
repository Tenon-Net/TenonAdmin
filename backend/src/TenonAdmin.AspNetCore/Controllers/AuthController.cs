using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 认证端点(设计 §4 认证模块 / §15 会话模型)。标准 [ApiController]——可按模块禁用后由用户同路由接管(设计 §5.4)。
/// Level3:refresh 进 HttpOnly Cookie,body 仅 accessToken;双提交 CSRF 由中间件校验。
/// 非 Level3:零变化,body 下发 refreshToken。
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService auth, ICaptchaService captcha, AuthCookieService cookies) : ControllerBase
{
    /// <summary>获取登录验证码(SVG)。匿名;返回票据 Id + SVG,登录时回传票据 Id + 输入码。</summary>
    [HttpGet("captcha")]
    [AllowAnonymous]
    public async Task<Result<CaptchaOutput>> Captcha() =>
        Result<CaptchaOutput>.Ok(await captcha.IssueAsync());

    /// <summary>账密登录。业务失败(密码错/停用)由 AdminExceptionFilter 转统一信封,这里只写成功路径。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> Login(LoginInput input)
    {
        var output = await auth.LoginAsync(input);
        return Result<LoginOutput>.Ok(cookies.ApplyAuthCookies(HttpContext, output));
    }

    /// <summary>短信二次验证完成登录:凭挑战 Id(登录 40009 信令下发)+ 短信码换令牌。</summary>
    [HttpPost("login/sms")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> LoginBySms(SmsChallengeLoginInput input)
    {
        var output = await auth.LoginBySmsChallengeAsync(input);
        return Result<LoginOutput>.Ok(cookies.ApplyAuthCookies(HttpContext, output));
    }

    /// <summary>TOTP 二次验证完成登录:凭挑战 Id(登录 40018 信令)+ 动态口令换令牌。</summary>
    [HttpPost("login/totp")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> LoginByTotp(TotpChallengeLoginInput input)
    {
        var output = await auth.LoginByTotpChallengeAsync(input);
        return Result<LoginOutput>.Ok(cookies.ApplyAuthCookies(HttpContext, output));
    }

    /// <summary>短信二次验证重发验证码(冷却/日上限由服务端强制)。</summary>
    [HttpPost("login/sms/resend")]
    [AllowAnonymous]
    public async Task<Result<SmsSendOutput>> ResendSms(SmsResendInput input) =>
        Result<SmsSendOutput>.Ok(await auth.ResendSmsChallengeAsync(input));

    /// <summary>免密登录发码(开关 <c>sys.security.smsLogin.enabled</c>;图形验证码启用时须携带;响应不区分手机号是否存在)。</summary>
    [HttpPost("sms/send")]
    [AllowAnonymous]
    public async Task<Result<SmsSendOutput>> SendSmsLoginCode(PhoneCodeInput input) =>
        Result<SmsSendOutput>.Ok(await auth.SendSmsLoginCodeAsync(input));

    /// <summary>免密登录:手机号 + 短信码换令牌。</summary>
    [HttpPost("sms/login")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> LoginByPhone(PhoneLoginInput input)
    {
        var output = await auth.LoginByPhoneAsync(input);
        return Result<LoginOutput>.Ok(cookies.ApplyAuthCookies(HttpContext, output));
    }

    /// <summary>
    /// 刷新令牌换发新令牌对(轮换 + 复用检测,§15)。匿名:访问令牌可能已过期,凭刷新令牌换发。
    /// Level3:body 可空,从 <c>tenon_rt</c> Cookie 读取;成功后轮换 Cookie/CSRF。
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> Refresh([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshInput? input = null)
    {
        var refresh = cookies.ResolveRefreshToken(HttpContext, input?.RefreshToken);
        var output = await auth.RefreshAsync(new RefreshInput { RefreshToken = refresh });
        return Result<LoginOutput>.Ok(cookies.ApplyAuthCookies(HttpContext, output));
    }

    /// <summary>登出:吊销当前会话(sid 取自令牌)。仅需认证,不挂具体权限码。Level3 同时清 Cookie。</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<Result<bool>> Logout()
    {
        var sessionId = User.FindFirstValue(TokenClaimNames.SESSION_ID);
        if (!string.IsNullOrEmpty(sessionId)) await auth.LogoutAsync(sessionId);
        cookies.ClearAuthCookies(HttpContext);
        if (cookies.IsCookieSessionEnabled)
            cookies.RotateCsrfCookie(HttpContext); // 登出后仍轮换一次,避免旧 CSRF 残留
        return Result<bool>.Ok(true);
    }
}
