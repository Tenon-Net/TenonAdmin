using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.GitHub;

/// <summary>
/// GitHub OAuth App 外部登录(卫星包)。Code 硬固定 <c>github</c>;Subject = 用户数字 id;
/// scope 仅 <c>read:user</c>。构造注入 <see cref="HttpClient"/>(H1),便于测试假 handler。
/// 网络/超时/非法 JSON 等非调用方取消异常统一映射为 <see cref="ErrorCode.OAuthExchangeFailed"/>。
/// </summary>
public class GitHubExternalAuthProvider : IExternalAuthProvider
{
    public const string FixedCode = "github";
    public const string HttpClientName = "TenonAdmin.Auth.GitHub";

    /// <summary>
    /// GitHub REST 要求有效 User-Agent;固定可识别产品名(无密钥/token)。
    /// 既挂在命名 HttpClient 默认头上,也在每条请求上再写一遍,避免仅依赖 DefaultRequestHeaders 的歧义。
    /// </summary>
    public const string UserAgentValue = "TenonAdmin-GitHubAuth";

    private readonly GitHubAuthOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<GitHubExternalAuthProvider> _logger;

    public GitHubExternalAuthProvider(
        GitHubAuthOptions options,
        HttpClient http,
        ILogger<GitHubExternalAuthProvider> logger)
    {
        _options = options;
        _http = http;
        _logger = logger;
        EnsureDefaultUserAgent(_http);
    }

    /// <summary>给命名客户端默认头补 UA(Setup 与 ctor 双保险)。</summary>
    public static void EnsureDefaultUserAgent(HttpClient http)
    {
        if (http.DefaultRequestHeaders.UserAgent.Count == 0
            && !http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgentValue);
        }
    }

    /// <summary>单请求强制带 UA(SendAsync 路径上可观测、测试可断言)。</summary>
    internal static void ApplyRequestUserAgent(HttpRequestMessage req)
    {
        if (!req.Headers.Contains("User-Agent"))
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgentValue);
    }

    /// <inheritdoc />
    public string Code => FixedCode;

    /// <inheritdoc />
    public string DisplayName =>
        string.IsNullOrWhiteSpace(_options.DisplayName) ? "GitHub" : _options.DisplayName.Trim();

    /// <inheritdoc />
    public string? Icon => _options.Icon;

    /// <inheritdoc />
    public virtual Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        var url = "https://github.com/login/oauth/authorize"
                  + $"?client_id={Uri.EscapeDataString(_options.ClientId)}"
                  + $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}"
                  + "&scope=read%3Auser"
                  + $"&state={Uri.EscapeDataString(request.State)}";
        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public virtual async Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await ExchangeCodeAsync(request.Code, request.RedirectUri, cancellationToken);
            return await FetchUserAsync(accessToken, cancellationToken);
        }
        catch (AdminException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 调用方取消:传播,不改写成交换失败
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        {
            _logger.LogWarning(ex, "GitHub OAuth exchange failed ({Type})", ex.GetType().Name);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
    }

    protected virtual async Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = content,
        };
        ApplyRequestUserAgent(req);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub token HTTP {Status}", (int)resp.StatusCode);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out _))
        {
            _logger.LogWarning("GitHub token error={Error}", root.TryGetProperty("error", out var e) ? e.GetString() : "?");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        var token = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("GitHub token response missing access_token");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return token;
    }

    protected virtual async Task<ExternalIdentity> FetchUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        ApplyRequestUserAgent(req);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub user HTTP {Status}", (int)resp.StatusCode);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var idEl))
        {
            _logger.LogWarning("GitHub user missing id");
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        var subject = ParseGitHubUserId(idEl);

        var login = root.TryGetProperty("login", out var l) ? l.GetString() : null;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var display = !string.IsNullOrWhiteSpace(login) ? login
            : !string.IsNullOrWhiteSpace(name) ? name
            : null;

        return new ExternalIdentity(FixedCode, subject, display, Email: null);
    }

    /// <summary>GitHub 用户 id → 规范十进制字符串;拒绝非正数/非数字/溢出。</summary>
    public static string ParseGitHubUserId(JsonElement idEl)
    {
        if (idEl.ValueKind == JsonValueKind.Number)
        {
            if (idEl.TryGetInt64(out var n) && n > 0)
                return n.ToString(CultureInfo.InvariantCulture);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        if (idEl.ValueKind == JsonValueKind.String)
        {
            var s = idEl.GetString();
            // 允许前导零等数字串,输出规范十进制(00123 → 123);拒绝非数字与 ≤0
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                return n.ToString(CultureInfo.InvariantCulture);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        throw new AdminException(ErrorCode.OAuthExchangeFailed);
    }
}
