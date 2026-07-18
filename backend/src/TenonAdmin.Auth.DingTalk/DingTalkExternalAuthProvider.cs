using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.DingTalk;

/// <summary>
/// 钉钉外部登录 provider(卫星包,批次 D)。PC 端<b>扫码登录</b>(钉钉新版 OAuth2 授权码):构造授权 URL →
/// 用户扫码授权后带 <c>code</c> 回调 → 换<b>用户</b> access_token → 拿 <c>unionId</c> 作为外部身份 <see cref="ExternalIdentity.Subject"/>。
/// <para>与企业微信不同,钉钉换的是每用户 access_token(无需缓存企业 token);裸 <see cref="HttpClient"/> 对接。方法 <c>virtual</c> 可覆写。</para>
/// </summary>
public class DingTalkExternalAuthProvider(DingTalkAuthOptions options, ILogger<DingTalkExternalAuthProvider> logger) : IExternalAuthProvider
{
    // 单例 provider,进程内复用一个 HttpClient(ponytail: 静态实例够用;需连接池精细控制再上 IHttpClientFactory)
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <inheritdoc />
    public string Code => options.Code;

    /// <inheritdoc />
    public string DisplayName => options.DisplayName;

    /// <inheritdoc />
    public string? Icon => options.Icon;

    /// <inheritdoc />
    public virtual Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        // 钉钉新版扫码登录授权(state 防 CSRF;scope=openid 拿基础身份;不使用 nonce/PKCE)
        var url = "https://login.dingtalk.com/oauth2/auth?response_type=code&scope=openid&prompt=consent" +
                  $"&client_id={Uri.EscapeDataString(options.AppKey)}" +
                  $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}" +
                  $"&state={Uri.EscapeDataString(request.State)}";
        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public virtual async Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
    {
        var accessToken = await GetUserAccessTokenAsync(request.Code, cancellationToken);
        return await GetUserIdentityAsync(accessToken, cancellationToken);
    }

    /// <summary>授权码换用户 access_token(钉钉 v1.0 OAuth2)。</summary>
    protected virtual async Task<string> GetUserAccessTokenAsync(string code, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            clientId = options.AppKey,
            clientSecret = options.AppSecret,
            code,
            grantType = "authorization_code",
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync("https://api.dingtalk.com/v1.0/oauth2/userAccessToken", content, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("钉钉 userAccessToken HTTP {Status}: {Body}", (int)resp.StatusCode, body);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        using var doc = JsonDocument.Parse(body);
        var token = StringProp(doc.RootElement, "accessToken");
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("钉钉 userAccessToken 响应缺 accessToken: {Body}", body);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return token;
    }

    /// <summary>用用户 access_token 拿身份(unionId 作唯一标识,附带昵称/邮箱/手机号)。</summary>
    protected virtual async Task<ExternalIdentity> GetUserIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.dingtalk.com/v1.0/contact/users/me");
        req.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        using var resp = await Http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("钉钉 users/me HTTP {Status}: {Body}", (int)resp.StatusCode, body);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var subject = StringProp(root, "unionId") ?? StringProp(root, "openId");
        if (string.IsNullOrEmpty(subject))
        {
            logger.LogWarning("钉钉 users/me 未返回 unionId/openId");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return new ExternalIdentity(options.Code, subject, StringProp(root, "nick"), StringProp(root, "email"), StringProp(root, "mobile"));
    }

    private static string? StringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) ? v.GetString() : null;
}
