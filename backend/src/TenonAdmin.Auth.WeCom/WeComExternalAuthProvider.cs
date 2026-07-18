using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.WeCom;

/// <summary>
/// 企业微信外部登录 provider(卫星包,批次 D)。PC 端<b>扫码授权登录</b>:构造扫码 URL → 用户扫码授权后带 <c>code</c> 回调 →
/// 用企业 access_token 拿 <c>userid</c> 作为外部身份 <see cref="ExternalIdentity.Subject"/>。
/// <para>不走 OIDC(无 id_token/nonce/PKCE);裸 <see cref="HttpClient"/> 对接企业微信 API。方法 <c>virtual</c> 可覆写。</para>
/// </summary>
public class WeComExternalAuthProvider(WeComAuthOptions options, ILogger<WeComExternalAuthProvider> logger) : IExternalAuthProvider
{
    // 单例 provider,进程内复用一个 HttpClient(ponytail: 静态实例够用;需连接池精细控制再上 IHttpClientFactory)
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // 企业 access_token 有额度且有效期 2h,不能每次登录都换 → 进程内缓存(ponytail: 每副本各缓存,多实例可接受)
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;

    /// <inheritdoc />
    public string Code => options.Code;

    /// <inheritdoc />
    public string DisplayName => options.DisplayName;

    /// <inheritdoc />
    public string? Icon => options.Icon;

    /// <inheritdoc />
    public virtual Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        // 企业微信 PC 扫码授权登录(state 防 CSRF;不使用 nonce/PKCE)
        var url = "https://login.work.weixin.qq.com/wwlogin/sso/login?login_type=CorpApp" +
                  $"&appid={Uri.EscapeDataString(options.CorpId)}" +
                  $"&agentid={Uri.EscapeDataString(options.AgentId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}" +
                  $"&state={Uri.EscapeDataString(request.State)}";
        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public virtual async Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var url = $"https://qyapi.weixin.qq.com/cgi-bin/auth/getuserinfo?access_token={token}&code={Uri.EscapeDataString(request.Code)}";
        using var doc = await GetJsonAsync(url, cancellationToken);
        var root = doc.RootElement;
        EnsureOk(root, "getuserinfo");

        // 企业成员回 userid;外部联系人回 openid。任取其一作为唯一标识。
        var subject = StringProp(root, "userid") ?? StringProp(root, "openid");
        if (string.IsNullOrEmpty(subject))
        {
            logger.LogWarning("企业微信 getuserinfo 未返回 userid/openid");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return new ExternalIdentity(options.Code, subject);
    }

    /// <summary>取企业 access_token(进程内缓存,提前 5 分钟过期刷新)。</summary>
    protected virtual async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry) return _cachedToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry) return _cachedToken;
            var url = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={Uri.EscapeDataString(options.CorpId)}&corpsecret={Uri.EscapeDataString(options.CorpSecret)}";
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
        using var resp = await Http.GetAsync(url, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("企业微信 API HTTP {Status}: {Body}", (int)resp.StatusCode, body);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return JsonDocument.Parse(body);
    }

    private void EnsureOk(JsonElement root, string api)
    {
        if (root.TryGetProperty("errcode", out var ec) && ec.GetInt32() != 0)
        {
            logger.LogWarning("企业微信 {Api} errcode={Code} errmsg={Msg}", api, ec.GetInt32(), StringProp(root, "errmsg"));
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
    }

    private static string? StringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) ? v.GetString() : null;
}
