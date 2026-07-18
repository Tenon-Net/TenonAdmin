using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 外部登录 / SSO 端点(批次 D)。<c>[Module("ExternalAuth")]</c> 可整段禁用。机密客户端:client secret 全程服务端持有,
/// 令牌<b>不进</b>重定向 URL(回调把令牌对存一次性票据,前端凭票据换取)。
/// <para>登录流(<c>[AllowAnonymous]</c>):<c>providers</c> 点亮按钮 → <c>{provider}/authorize</c> 302 到 IdP →
/// <c>{provider}/callback</c> 验 state/换身份/建会话/存票据/302 回前端 → <c>exchange</c> 票据换令牌。
/// 绑定流(<c>[ActiveSession]</c>,个人中心):<c>{provider}/bind</c> 发起 → 同一回调按 state.mode=bind 绑到当前用户;<c>bindings</c> 查、<c>{provider}/binding</c> 解绑。</para>
/// </summary>
[ApiController]
[Route("api/v1/auth/external")]
[Module("ExternalAuth")]
public class ExternalAuthController(
    IEnumerable<IExternalAuthProvider> externalProviders,
    ISysUserExternalService externalBindings,
    IAuthService auth,
    ICacheProvider cache,
    ICurrentUser currentUser,
    AdminExternalAuthOptions options) : ControllerBase
{
    private static readonly TimeSpan STATE_TTL = TimeSpan.FromMinutes(5);      // 授权→回调往返窗口
    private static readonly TimeSpan TICKET_TTL = TimeSpan.FromSeconds(120);   // 回调→前端换令牌窗口

    private long CurrentUserId => currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);

    /// <summary>登录页可用的外部登录方式(仅回 code/名称/图标;读运营启用开关过滤)。</summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    public async Task<Result<IReadOnlyList<ExternalProviderItem>>> Providers()
    {
        var items = new List<ExternalProviderItem>();
        foreach (var p in externalProviders)
            if (await externalBindings.IsEnabledAsync(p.Code))
                items.Add(new ExternalProviderItem { Code = p.Code, DisplayName = p.DisplayName, Icon = p.Icon });
        return Result<IReadOnlyList<ExternalProviderItem>>.Ok(items);
    }

    /// <summary>发起登录:生成 state/nonce/PKCE 存缓存,302 跳转到 IdP 授权端点(顶层浏览器导航)。</summary>
    [HttpGet("{provider}/authorize")]
    [AllowAnonymous]
    public async Task<IActionResult> Authorize(string provider, CancellationToken cancellationToken)
    {
        var p = await ResolveEnabledProviderAsync(provider);
        var url = await StartAuthorizeAsync(p, mode: "login", userId: null, cancellationToken);
        return Redirect(url);
    }

    /// <summary>IdP 回调:验 state(CSRF)→ 按模式 登录/绑定 → 302 回前端结果页(带 ticket / bind / error)。</summary>
    [HttpGet("{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        // IdP 侧报错(用户拒绝授权等)或缺参
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return Redirect(FrontendResultUrl($"error={(int)ErrorCode.OAuthExchangeFailed}"));

        var st = await cache.GetAndRemoveAsync<ExternalOAuthState>(CacheKeys.OAuthState(state), cancellationToken);
        if (st is null || st.ProviderCode != provider)
            return Redirect(FrontendResultUrl($"error={(int)ErrorCode.OAuthStateInvalid}"));

        try
        {
            if (st.Mode == "bind")
            {
                await HandleBindCallbackAsync(st, code!, cancellationToken);
                return Redirect(FrontendResultUrl($"bind={Uri.EscapeDataString(provider)}"));
            }

            var output = await auth.LoginByExternalAsync(new ExternalLoginInput
            {
                ProviderCode = provider,
                Code = code!,
                CodeVerifier = st.CodeVerifier,
                Nonce = st.Nonce,
                RedirectUri = st.RedirectUri,
            });
            var ticket = RandomToken();
            await cache.SetAsync(CacheKeys.OAuthTicket(ticket), output, TICKET_TTL, cancellationToken);
            return Redirect(FrontendResultUrl($"ticket={ticket}"));
        }
        catch (AdminException ex)
        {
            // 登录/绑定失败也回前端(错误码在 URL,前端按码查文案);避免把裸异常页甩给用户
            return Redirect(FrontendResultUrl($"error={(int)ex.Code}"));
        }
    }

    /// <summary>一次性票据换令牌(登录回调后前端调用;票据无效/过期/已用抛 40014)。</summary>
    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> Exchange(ExternalExchangeInput input, CancellationToken cancellationToken)
    {
        var output = await cache.GetAndRemoveAsync<LoginOutput>(CacheKeys.OAuthTicket(input.Ticket), cancellationToken);
        AdminException.ThrowIf(output is null, ErrorCode.OAuthStateInvalid);
        return Result<LoginOutput>.Ok(output!);
    }

    /// <summary>【个人中心】看自己已绑定的外部账号。</summary>
    [HttpGet("bindings")]
    [ActiveSession]
    public async Task<Result<IReadOnlyList<ExternalBindingItem>>> Bindings()
    {
        var list = await externalBindings.ListByUserAsync(CurrentUserId);
        IReadOnlyList<ExternalBindingItem> items = [.. list.Select(x => new ExternalBindingItem
        {
            Provider = x.Provider,
            DisplayName = x.DisplayName,
            BoundAt = x.CreateTime,
        })];
        return Result<IReadOnlyList<ExternalBindingItem>>.Ok(items);
    }

    /// <summary>【个人中心】发起绑定:走一次 OAuth 往返证明当前用户拥有该外部身份;返回授权 URL 由前端跳转。</summary>
    [HttpPost("{provider}/bind")]
    [ActiveSession]
    [OperationLog("发起外部账号绑定")]
    public async Task<Result<ExternalBindStartOutput>> BindStart(string provider, CancellationToken cancellationToken)
    {
        var p = await ResolveEnabledProviderAsync(provider);
        var url = await StartAuthorizeAsync(p, mode: "bind", userId: CurrentUserId, cancellationToken);
        return Result<ExternalBindStartOutput>.Ok(new ExternalBindStartOutput { AuthorizeUrl = url });
    }

    /// <summary>【个人中心】解绑某 provider 的外部账号。</summary>
    [HttpDelete("{provider}/binding")]
    [ActiveSession]
    [OperationLog("解绑外部账号")]
    public async Task<Result<bool>> Unbind(string provider)
    {
        await externalBindings.UnbindAsync(CurrentUserId, provider);
        return Result<bool>.Ok(true);
    }

    // ── 内部 ─────────────────────────────────────────────────────────────

    /// <summary>按 code 选出已启用的 provider(不存在 / 被运营关掉抛 40013)。</summary>
    protected virtual async Task<IExternalAuthProvider> ResolveEnabledProviderAsync(string provider)
    {
        var p = externalProviders.FirstOrDefault(x => x.Code == provider);
        AdminException.ThrowIf(p is null || !await externalBindings.IsEnabledAsync(provider), ErrorCode.OAuthProviderDisabled);
        return p!;
    }

    /// <summary>生成 state/nonce/PKCE、存授权态、返回 IdP 授权 URL(登录与绑定共用)。</summary>
    protected virtual async Task<string> StartAuthorizeAsync(IExternalAuthProvider provider, string mode, long? userId, CancellationToken cancellationToken)
    {
        var state = RandomToken();
        var nonce = RandomToken();
        var verifier = RandomToken();
        var redirectUri = CallbackUri(provider.Code);

        await cache.SetAsync(CacheKeys.OAuthState(state), new ExternalOAuthState
        {
            Mode = mode,
            ProviderCode = provider.Code,
            Nonce = nonce,
            CodeVerifier = verifier,
            RedirectUri = redirectUri,
            UserId = userId,
        }, STATE_TTL, cancellationToken);

        return await provider.BuildAuthorizeUrlAsync(
            new ExternalAuthorizeRequest(state, nonce, CodeChallenge(verifier), redirectUri), cancellationToken);
    }

    /// <summary>绑定回调:换外部身份 → 绑到 state 记录的当前用户(已被他人占用抛 40017)。</summary>
    protected virtual async Task HandleBindCallbackAsync(ExternalOAuthState st, string code, CancellationToken cancellationToken)
    {
        var provider = externalProviders.FirstOrDefault(x => x.Code == st.ProviderCode);
        AdminException.ThrowIf(provider is null, ErrorCode.OAuthProviderDisabled);
        var identity = await provider!.ExchangeAsync(
            new ExternalExchangeRequest(code, st.CodeVerifier, st.Nonce, st.RedirectUri), cancellationToken);
        await externalBindings.BindAsync(st.UserId!.Value, identity);
    }

    /// <summary>回调地址:配了 <c>CallbackBaseUrl</c> 用之(代理下务必配),否则从请求推断。</summary>
    protected virtual string CallbackUri(string provider)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.CallbackBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : options.CallbackBaseUrl!.TrimEnd('/');
        return $"{baseUrl}/api/v1/auth/external/{provider}/callback";
    }

    /// <summary>前端结果页 URL(相对路径=同源部署,绝对 URL=前后端分离 dev);追加查询参数。</summary>
    protected virtual string FrontendResultUrl(string query)
    {
        var basePath = options.FrontendResultPath;
        var sep = basePath.Contains('?') ? '&' : '?';
        return $"{basePath}{sep}{query}";
    }

    /// <summary>256 位加密随机 → base64url(state/nonce/PKCE verifier/票据通用)。</summary>
    protected static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>PKCE code_challenge = base64url(SHA256(verifier))(S256)。</summary>
    protected static string CodeChallenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
