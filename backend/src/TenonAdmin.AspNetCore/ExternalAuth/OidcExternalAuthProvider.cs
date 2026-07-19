using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 内核内置的标准 OIDC 授权码登录 provider(设计批次 D)。通吃 Keycloak / Entra ID / Authing / Auth0 等
/// 任何合规 OpenID Connect IdP。<b>零新包</b>:发现文档 / JWKS / id_token 验签全用 JwtBearer 已传递的
/// <c>Microsoft.IdentityModel.*</c>(<see cref="ConfigurationManager{T}"/> + <see cref="JsonWebTokenHandler"/>);
/// token 端点交换用 <see cref="IHttpClientFactory"/>(ASP.NET Core 共享框架内)。
/// <para>每个 appsettings 里配置的 OIDC 条目实例化一个本类(各持自己的 authority/clientId);按 <see cref="Code"/> 选型。
/// 方法 <c>virtual</c>:换 token 端点鉴权方式(如 client_secret_basic)、加自定义 claim 映射,继承覆写即可。</para>
/// </summary>
public class OidcExternalAuthProvider : IExternalAuthProvider
{
    // 无状态、线程安全,可复用(同 JwtTokenProvider)
    private static readonly JsonWebTokenHandler HANDLER = new();

    /// <summary>OIDC 连接配置(authority / clientId / secret / scopes / PKCE 开关)</summary>
    protected OidcProviderOptions Options { get; }

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OidcExternalAuthProvider> _logger;

    // 每实例一个 ConfigurationManager:自动拉取 + 缓存发现文档/JWKS,签名密钥轮换时自愈(设计文档核对确认)
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;

    public OidcExternalAuthProvider(OidcProviderOptions options, IHttpClientFactory httpFactory, ILogger<OidcExternalAuthProvider> logger, bool allowHttpMetadata = false)
    {
        Options = options;
        _httpFactory = httpFactory;
        _logger = logger;

        var metadataAddress = options.Authority.TrimEnd('/') + "/.well-known/openid-configuration";
        // 生产强制 https 元数据(fail-closed):allowHttpMetadata 仅开发环境为 true,才放行 http 本地 IdP。
        // 生产即便误配 http authority 也拒明文发现文档 —— 否则 MITM 可伪造 JWKS → 伪造 id_token 冒充任意账号。
        var requireHttps = !allowHttpMetadata || metadataAddress.StartsWith("https", StringComparison.OrdinalIgnoreCase);
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = requireHttps });
    }

    /// <inheritdoc />
    public string Code => Options.Code;

    /// <inheritdoc />
    public string DisplayName => Options.DisplayName;

    /// <inheritdoc />
    public string? Icon => Options.Icon;

    /// <inheritdoc />
    public virtual async Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = await _configManager.GetConfigurationAsync(cancellationToken);

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = Options.ClientId,
            ["redirect_uri"] = request.RedirectUri,
            ["scope"] = Options.Scopes,
            ["state"] = request.State,
            ["nonce"] = request.Nonce,
        };
        if (Options.UsePkce)
        {
            query["code_challenge"] = request.CodeChallenge;
            query["code_challenge_method"] = "S256";
        }

        var qs = string.Join('&', query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
        var sep = cfg.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        return $"{cfg.AuthorizationEndpoint}{sep}{qs}";
    }

    /// <inheritdoc />
    public virtual async Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = await _configManager.GetConfigurationAsync(cancellationToken);
        var idToken = await ExchangeCodeForIdTokenAsync(cfg, request, cancellationToken);
        var claims = await ValidateIdTokenAsync(cfg, idToken, request.Nonce, cancellationToken);

        var subject = ClaimValue(claims, "sub");
        if (string.IsNullOrEmpty(subject))
        {
            _logger.LogWarning("OIDC[{Code}] id_token 缺少 sub 声明", Options.Code);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        return new ExternalIdentity(
            Provider: Options.Code,
            Subject: subject,
            DisplayName: ClaimValue(claims, "name") ?? ClaimValue(claims, "preferred_username"),
            Email: ClaimValue(claims, "email"),
            Phone: ClaimValue(claims, "phone_number"));
    }

    /// <summary>授权码换 token,取出 id_token 原文(ponytail: 用 client_secret_post 体内带密钥,主流 IdP 通吃;需 client_secret_basic 覆写此步)</summary>
    protected virtual async Task<string> ExchangeCodeForIdTokenAsync(OpenIdConnectConfiguration cfg, ExternalExchangeRequest request, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = request.Code,
            ["redirect_uri"] = request.RedirectUri,
            ["client_id"] = Options.ClientId,
            ["client_secret"] = Options.ClientSecret,
        };
        if (Options.UsePkce)
            form["code_verifier"] = request.CodeVerifier;

        using var http = _httpFactory.CreateClient();
        using var resp = await http.PostAsync(cfg.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("OIDC[{Code}] token 交换失败 {Status}: {Body}", Options.Code, (int)resp.StatusCode, body);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("id_token", out var el) && el.GetString() is { Length: > 0 } idToken)
            return idToken;

        _logger.LogWarning("OIDC[{Code}] token 响应缺 id_token", Options.Code);
        throw new AdminException(ErrorCode.OAuthExchangeFailed);
    }

    /// <summary>验 id_token(签名/issuer/audience/exp 由 JWKS,nonce 手工比对防重放),返回声明字典</summary>
    protected virtual async Task<IDictionary<string, object>> ValidateIdTokenAsync(OpenIdConnectConfiguration cfg, string idToken, string expectedNonce, CancellationToken cancellationToken)
    {
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = cfg.Issuer,
            ValidAudience = Options.ClientId,
            IssuerSigningKeys = cfg.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };

        var result = await HANDLER.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid)
        {
            _logger.LogWarning(result.Exception, "OIDC[{Code}] id_token 校验失败", Options.Code);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }

        // nonce 防重放:TokenValidationParameters 不管 nonce(那是 OIDC 层职责),手工比对授权阶段留存值
        if (ClaimValue(result.Claims, "nonce") != expectedNonce)
        {
            _logger.LogWarning("OIDC[{Code}] id_token nonce 不匹配(疑似重放)", Options.Code);
            throw new AdminException(ErrorCode.OAuthExchangeFailed);
        }
        return result.Claims;
    }

    /// <summary>取声明字符串值(缺失返回 null)</summary>
    protected static string? ClaimValue(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var v) ? v?.ToString() : null;
}
