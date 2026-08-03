using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.WeChat;

/// <summary>
/// 个人微信开放平台网站应用扫码登录。Code=<c>wechat</c>;Subject <b>仅 unionid</b>(S-A);
/// 本批不调 userinfo(W-n2)。HttpClient 注入(H1)。
/// 网络/超时/非法 JSON 等非调用方取消异常统一映射为 <see cref="ErrorCode.OAuthExchangeFailed"/>。
/// </summary>
public class WeChatExternalAuthProvider : IExternalAuthProvider
{
    public const string FixedCode = "wechat";
    public const string HttpClientName = "TenonAdmin.Auth.WeChat";

    private readonly WeChatAuthOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<WeChatExternalAuthProvider> _logger;

    public WeChatExternalAuthProvider(
        WeChatAuthOptions options,
        HttpClient http,
        ILogger<WeChatExternalAuthProvider> logger)
    {
        _options = options;
        _http = http;
        _logger = logger;
    }

    public string Code => FixedCode;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(_options.DisplayName) ? "微信" : _options.DisplayName.Trim();

    public string? Icon => _options.Icon;

    public virtual Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        // 开放平台网站应用扫码:response_type=code scope=snsapi_login + #wechat_redirect
        var url = "https://open.weixin.qq.com/connect/qrconnect"
                  + $"?appid={Uri.EscapeDataString(_options.AppId)}"
                  + $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}"
                  + "&response_type=code"
                  + "&scope=snsapi_login"
                  + $"&state={Uri.EscapeDataString(request.State)}"
                  + "#wechat_redirect";
        return Task.FromResult(url);
    }

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            // 不记完整 token URL(含 secret);只记异常类型
            _logger.LogWarning(ex, "WeChat OAuth exchange failed ({Type})", ex.GetType().Name);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
    }

    private async Task<ExternalIdentity> ExchangeCoreAsync(ExternalExchangeRequest request, CancellationToken cancellationToken)
    {
        // secret 在 query 为微信契约;日志禁止完整 URL
        var url = "https://api.weixin.qq.com/sns/oauth2/access_token"
                  + $"?appid={Uri.EscapeDataString(_options.AppId)}"
                  + $"&secret={Uri.EscapeDataString(_options.AppSecret)}"
                  + $"&code={Uri.EscapeDataString(request.Code)}"
                  + "&grant_type=authorization_code";

        using var resp = await _http.GetAsync(url, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("WeChat token HTTP {Status}", (int)resp.StatusCode);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var ec) && ec.GetInt32() != 0)
        {
            _logger.LogWarning("WeChat token errcode={Code} errmsg={Msg}",
                ec.GetInt32(),
                root.TryGetProperty("errmsg", out var em) ? em.GetString() : null);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        var unionid = root.TryGetProperty("unionid", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(unionid))
        {
            _logger.LogWarning("WeChat token missing unionid (openid-only rejected by S-A)");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        // Identity DisplayName 本批恒 null(不调 userinfo)
        return new ExternalIdentity(FixedCode, unionid.Trim(), DisplayName: null, Email: null);
    }
}
