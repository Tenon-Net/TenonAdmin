using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 浏览器会话 Cookie 写入/清除 + CSRF 双提交令牌。
/// 由 <c>TenonAdmin:Security:Session:CookieMode</c> 控制(ADR 0006);
/// 过渡期历史 <c>Profile=Level3</c> 仍视为开启(见 <see cref="AdminSecurityOptions.IsCookieSessionEnabled"/>)。
/// </summary>
public class AuthCookieService(AdminSecurityOptions security, IHostEnvironment env)
{
    /// <summary>是否启用 Cookie 会话模型</summary>
    public bool IsCookieSessionEnabled => security.IsCookieSessionEnabled;

    /// <summary>
    /// 登录/刷新成功后:把 refresh 写入 HttpOnly Cookie,CSRF 写入可读 Cookie;
    /// 从 JSON 体中清空 refreshToken(保留 accessToken)。
    /// </summary>
    public LoginOutput ApplyAuthCookies(HttpContext http, LoginOutput output)
    {
        if (!IsCookieSessionEnabled)
        {
            // 显式声明 body 模式(可选字段;旧客户端忽略 null 亦可,此处给新前端可辨别信号)
            return output with
            {
                SessionMode = "body",
                CsrfRequired = false,
            };
        }

        var refreshExp = output.RefreshExpiresAt;
        SetRefreshCookie(http, output.RefreshToken, refreshExp);
        RotateCsrfCookie(http, refreshExp);

        // body 不再下发 refresh(空串保留字段形状,避免破坏既有反序列化)
        return output with
        {
            RefreshToken = "",
            SessionMode = "cookie",
            CsrfRequired = true,
        };
    }

    /// <summary>登出/会话失效:清除 refresh 与 CSRF Cookie。</summary>
    public void ClearAuthCookies(HttpContext http)
    {
        if (!IsCookieSessionEnabled) return;
        DeleteCookie(http, AuthCookieNames.RefreshToken);
        DeleteCookie(http, AuthCookieNames.Csrf);
    }

    /// <summary>
    /// 解析刷新令牌:优先请求体;Level3 且 body 空时读 Cookie。
    /// 非 Level3 绝不读 Cookie(兼容旧客户端)。
    /// </summary>
    public string ResolveRefreshToken(HttpContext http, string? bodyRefreshToken)
    {
        if (!string.IsNullOrEmpty(bodyRefreshToken)) return bodyRefreshToken;
        if (!IsCookieSessionEnabled) return bodyRefreshToken ?? "";
        return http.Request.Cookies[AuthCookieNames.RefreshToken] ?? "";
    }

    /// <summary>是否携带了 refresh Cookie(判定「使用 Cookie 会话」以启用 CSRF 校验)。</summary>
    public bool HasRefreshCookie(HttpContext http) =>
        IsCookieSessionEnabled
        && !string.IsNullOrEmpty(http.Request.Cookies[AuthCookieNames.RefreshToken]);

    /// <summary>
    /// 双提交 CSRF:Cookie 与请求头常量时间比较。
    /// 返回 true = 通过;false = 应拒绝。
    /// </summary>
    public bool ValidateCsrf(HttpContext http)
    {
        var cookie = http.Request.Cookies[AuthCookieNames.Csrf];
        if (string.IsNullOrEmpty(cookie)) return false;
        if (!http.Request.Headers.TryGetValue(AuthCookieNames.CsrfHeader, out var headerVals))
            return false;
        var header = headerVals.ToString();
        if (string.IsNullOrEmpty(header)) return false;
        // 常量时间比较,防时序旁路
        var a = System.Text.Encoding.UTF8.GetBytes(cookie);
        var b = System.Text.Encoding.UTF8.GetBytes(header);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>轮换 CSRF Cookie(登录/刷新/登出生命周期)。</summary>
    public void RotateCsrfCookie(HttpContext http, DateTimeOffset? expires = null)
    {
        var token = Base64UrlToken(32);
        var opts = BuildCookieOptions(http, httpOnly: false, expires);
        http.Response.Cookies.Append(AuthCookieNames.Csrf, token, opts);
    }

    private void SetRefreshCookie(HttpContext http, string refreshToken, DateTimeOffset expires)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var opts = BuildCookieOptions(http, httpOnly: true, expires);
        http.Response.Cookies.Append(AuthCookieNames.RefreshToken, refreshToken, opts);
    }

    private CookieOptions BuildCookieOptions(HttpContext http, bool httpOnly, DateTimeOffset? expires)
    {
        // Cookie 模式:Secure + SameSite=Lax + Path=/;Domain 非空时跨子域(常配 SameSite=None)。
        // 生产须经 HTTPS 边缘,否则浏览器拒存。
        _ = http;
        _ = env;
        var domain = security.ResolveCookieDomain();
        return new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = true,
            SameSite = string.IsNullOrEmpty(domain) ? SameSiteMode.Lax : SameSiteMode.None,
            Path = "/",
            Domain = string.IsNullOrEmpty(domain) ? null : domain,
            Expires = expires?.UtcDateTime,
            IsEssential = true,
        };
    }

    private void DeleteCookie(HttpContext http, string name)
    {
        // 必须与 Set 时 Domain/Path/Secure/SameSite 一致,否则共享域 Cookie 删不掉
        var opts = BuildCookieOptions(
            http,
            httpOnly: name == AuthCookieNames.RefreshToken,
            expires: DateTimeOffset.UnixEpoch);
        http.Response.Cookies.Delete(name, opts);
    }

    private static string Base64UrlToken(int bytes)
    {
        var raw = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
