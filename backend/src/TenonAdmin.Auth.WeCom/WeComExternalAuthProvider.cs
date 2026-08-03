using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.WeCom;

/// <summary>
/// 企业微信外部登录 provider(卫星包,批次 D)。PC 端<b>扫码授权登录</b>:构造扫码 URL → 用户扫码授权后带 <c>code</c> 回调 →
/// 用企业 access_token 拿 <c>userid</c> 作为外部身份 <see cref="ExternalIdentity.Subject"/>。
/// <para>不走 OIDC(无 id_token/nonce/PKCE);<see cref="HttpClient"/> 由 DI 注入(与 GitHub/WeChat 同 H1 成法)。方法 <c>virtual</c> 可覆写。</para>
/// </summary>
public class WeComExternalAuthProvider : IExternalAuthProvider
{
    public const string HttpClientName = "TenonAdmin.Auth.WeCom";

    private readonly WeComAuthOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<WeComExternalAuthProvider> _logger;

    // 企业 access_token 有额度且有效期 2h,不能每次登录都换 → 实例内缓存(每 DI 单例一份)
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;

    public WeComExternalAuthProvider(
        WeComAuthOptions options,
        HttpClient http,
        ILogger<WeComExternalAuthProvider> logger)
    {
        _options = options;
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Code => _options.Code;

    /// <inheritdoc />
    public string DisplayName =>
        string.IsNullOrWhiteSpace(_options.DisplayName) ? "企业微信" : _options.DisplayName.Trim();

    /// <inheritdoc />
    public string? Icon => _options.Icon;

    /// <inheritdoc />
    public virtual Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        // 企业微信 PC 扫码授权登录(state 防 CSRF;不使用 nonce/PKCE)
        var url = "https://login.work.weixin.qq.com/wwlogin/sso/login?login_type=CorpApp" +
                  $"&appid={Uri.EscapeDataString(_options.CorpId)}" +
                  $"&agentid={Uri.EscapeDataString(_options.AgentId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}" +
                  $"&state={Uri.EscapeDataString(request.State)}";
        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public virtual async Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExchangeCoreAsync(request, cancellationToken);
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
                                       or InvalidOperationException or FormatException or ArgumentException
                                       or KeyNotFoundException)
        {
            // 对齐 GitHub:网络/解析类异常 → 40015,勿 500;不记含 secret 的 URL
            _logger.LogWarning(ex, "WeCom OAuth exchange failed ({Type})", ex.GetType().Name);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
    }

    private async Task<ExternalIdentity> ExchangeCoreAsync(ExternalExchangeRequest request, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var url = $"https://qyapi.weixin.qq.com/cgi-bin/auth/getuserinfo?access_token={Uri.EscapeDataString(token)}&code={Uri.EscapeDataString(request.Code)}";
        using var doc = await GetJsonAsync(url, cancellationToken);
        var root = doc.RootElement;
        EnsureOk(root, "getuserinfo");

        // 企业成员回 userid;外部联系人回 openid。任取其一作为唯一标识。
        var subject = StringProp(root, "userid") ?? StringProp(root, "openid");
        if (string.IsNullOrEmpty(subject))
        {
            _logger.LogWarning("企业微信 getuserinfo 未返回 userid/openid");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        // DisplayName 需再调 user/get,本批不扩;pending-link 确认框仍可用 provider 展示名
        return new ExternalIdentity(_options.Code, subject);
    }

    /// <summary>取企业 access_token(实例内缓存,提前 5 分钟过期刷新)。</summary>
    protected virtual async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry) return _cachedToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry) return _cachedToken;
            // corpsecret 走 query 是企业微信 gettoken 的 GET API 契约;日志禁止完整 URL
            var url = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={Uri.EscapeDataString(_options.CorpId)}&corpsecret={Uri.EscapeDataString(_options.CorpSecret)}";
            using var doc = await GetJsonAsync(url, cancellationToken);
            var root = doc.RootElement;
            EnsureOk(root, "gettoken");
            _cachedToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 7200;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 300));
            return _cachedToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var resp = await _http.GetAsync(url, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("企业微信 API HTTP {Status}", (int)resp.StatusCode);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return JsonDocument.Parse(body);
    }

    private void EnsureOk(JsonElement root, string api)
    {
        if (root.TryGetProperty("errcode", out var ec) && ec.GetInt32() != 0)
        {
            _logger.LogWarning("企业微信 {Api} errcode={Code} errmsg={Msg}", api, ec.GetInt32(), StringProp(root, "errmsg"));
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
    }

    private static string? StringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) ? v.GetString() : null;
}
