using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.DingTalk;

/// <summary>
/// 钉钉外部登录 provider(卫星包,批次 D)。PC 端<b>扫码登录</b>(钉钉新版 OAuth2 授权码):构造授权 URL →
/// 用户扫码授权后带 <c>code</c> 回调 → 换用户 access_token → 拿 <c>unionId</c> 作为外部身份 <see cref="ExternalIdentity.Subject"/>。
/// <para><see cref="HttpClient"/> 由 DI 注入(与 GitHub/WeChat 同 H1 成法)。方法 <c>virtual</c> 可覆写。</para>
/// </summary>
public class DingTalkExternalAuthProvider : IExternalAuthProvider
{
    public const string HttpClientName = "TenonAdmin.Auth.DingTalk";

    private readonly DingTalkAuthOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<DingTalkExternalAuthProvider> _logger;

    public DingTalkExternalAuthProvider(
        DingTalkAuthOptions options,
        HttpClient http,
        ILogger<DingTalkExternalAuthProvider> logger)
    {
        _options = options;
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Code => _options.Code;

    /// <inheritdoc />
    public string DisplayName =>
        string.IsNullOrWhiteSpace(_options.DisplayName) ? "钉钉" : _options.DisplayName.Trim();

    /// <inheritdoc />
    public string? Icon => _options.Icon;

    /// <inheritdoc />
    public virtual Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        // 钉钉新版扫码登录授权(state 防 CSRF;scope=openid 拿基础身份;prompt=consent 便于联调看清授权页)
        var url = "https://login.dingtalk.com/oauth2/auth?response_type=code&scope=openid&prompt=consent" +
                  $"&client_id={Uri.EscapeDataString(_options.AppKey)}" +
                  $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}" +
                  $"&state={Uri.EscapeDataString(request.State)}";
        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public virtual async Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await GetUserAccessTokenAsync(request.Code, cancellationToken);
            return await GetUserIdentityAsync(accessToken, cancellationToken);
        }
        catch (AdminException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or InvalidOperationException or FormatException or ArgumentException)
        {
            // 对齐 GitHub:网络/解析类异常 → 40015,勿 500
            _logger.LogWarning(ex, "DingTalk OAuth exchange failed ({Type})", ex.GetType().Name);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
    }

    /// <summary>授权码换用户 access_token(钉钉 v1.0 OAuth2)。</summary>
    protected virtual async Task<string> GetUserAccessTokenAsync(string code, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            clientId = _options.AppKey,
            clientSecret = _options.AppSecret,
            code,
            grantType = "authorization_code",
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("https://api.dingtalk.com/v1.0/oauth2/userAccessToken", content, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("钉钉 userAccessToken HTTP {Status}", (int)resp.StatusCode);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        using var doc = JsonDocument.Parse(body);
        var token = StringProp(doc.RootElement, "accessToken");
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("钉钉 userAccessToken 响应缺 accessToken");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return token;
    }

    /// <summary>用用户 access_token 拿身份(unionId 作唯一标识,附带昵称/邮箱/手机号 → pending-link 确认框可用 DisplayName)。</summary>
    protected virtual async Task<ExternalIdentity> GetUserIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.dingtalk.com/v1.0/contact/users/me");
        req.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("钉钉 users/me HTTP {Status}", (int)resp.StatusCode);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var subject = StringProp(root, "unionId") ?? StringProp(root, "openId");
        if (string.IsNullOrEmpty(subject))
        {
            _logger.LogWarning("钉钉 users/me 未返回 unionId/openId");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return new ExternalIdentity(
            _options.Code,
            subject,
            StringProp(root, "nick"),
            StringProp(root, "email"),
            StringProp(root, "mobile"));
    }

    private static string? StringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) ? v.GetString() : null;
}
