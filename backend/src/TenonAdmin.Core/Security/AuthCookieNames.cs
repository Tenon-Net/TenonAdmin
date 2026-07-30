namespace TenonAdmin.Core;

/// <summary>
/// Level3 浏览器会话 Cookie / CSRF 常量(禁硬编码字符串纪律)。
/// 仅 <see cref="SecurityProfile.Level3"/> 时启用;非 Level3 仍走请求体 refresh。
/// </summary>
public static class AuthCookieNames
{
    /// <summary>HttpOnly 刷新令牌 Cookie</summary>
    public const string RefreshToken = "tenon_rt";

    /// <summary>可读 CSRF Cookie(双提交)</summary>
    public const string Csrf = "tenon_csrf";

    /// <summary>客户端须回传的 CSRF 请求头</summary>
    public const string CsrfHeader = "X-Tenon-CSRF";
}
