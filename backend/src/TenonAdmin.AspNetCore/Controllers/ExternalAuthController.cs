using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
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
    AdminExternalAuthOptions options,
    IWebHostEnvironment env,
    AuthCookieService cookies) : ControllerBase
{
    private static readonly TimeSpan STATE_TTL = TimeSpan.FromMinutes(5);      // 授权→回调往返窗口
    private static readonly TimeSpan TICKET_TTL = TimeSpan.FromSeconds(120);   // 回调→前端换令牌窗口
    private static readonly TimeSpan PENDING_LINK_TTL = TimeSpan.FromMinutes(15); // 未绑定→账密登录后认领窗口
    private const string STATE_COOKIE = "tn_oauth_state";                      // 登录态 binder cookie(防登录 CSRF)
    private const string PENDING_COOKIE = "tn_oauth_pending";                  // pending-link binder(防换浏览器抢绑)

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

    /// <summary>
    /// 管理端:全部已注册 provider(含已禁用)+ enabled。供系统配置「第三方登录」Tab 开关登录页显示。
    /// 权限码 = 本路由;种子挂在系统配置菜单下。
    /// </summary>
    [HttpGet("providers/all")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<ExternalProviderAdminItem>>> ProvidersAll()
    {
        var items = new List<ExternalProviderAdminItem>();
        foreach (var p in externalProviders)
        {
            items.Add(new ExternalProviderAdminItem
            {
                Code = p.Code,
                DisplayName = p.DisplayName,
                Icon = p.Icon,
                Enabled = await externalBindings.IsEnabledAsync(p.Code),
            });
        }
        return Result<IReadOnlyList<ExternalProviderAdminItem>>.Ok(items);
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

        // 登录 CSRF 防御:state 必须由发起 /authorize 的<b>同一浏览器</b>回调——比对授权阶段下发的 binder cookie。
        // 缺 cookie / 不匹配 = 疑似他人预先拼好的 (code,state) 诱导受害者登入攻击者账号 → 拒。
        // bind 模式豁免:目标账号已由 [ActiveSession] 的 st.UserId 服务端锁定,不吃回调 CSRF。
        if (st.Mode != "bind")
        {
            var binder = Request.Cookies[STATE_COOKIE];
            Response.Cookies.Delete(STATE_COOKIE, new CookieOptions { Path = "/api/v1/auth/external" });
            if (string.IsNullOrEmpty(binder) || !FixedTimeEquals(binder, st.Binder))
                return Redirect(FrontendResultUrl($"error={(int)ErrorCode.OAuthStateInvalid}"));
        }

        try
        {
            if (st.Mode == "bind")
            {
                await HandleBindCallbackAsync(st, code!, cancellationToken);
                return Redirect(FrontendResultUrl($"bind={Uri.EscapeDataString(provider)}"));
            }

            // 登录:先 Exchange 一次(授权码单次),再按已解析身份登录/开户;未绑定 reject → pending-link 而非死 40016。
            var p = await ResolveEnabledProviderAsync(provider);
            var identity = await p.ExchangeAsync(
                new ExternalExchangeRequest(code!, st.CodeVerifier, st.Nonce, st.RedirectUri), cancellationToken);
            try
            {
                var output = await auth.LoginByExternalIdentityAsync(identity, cancellationToken);
                var ticket = RandomToken();
                await cache.SetAsync(CacheKeys.OAuthTicket(ticket), output, TICKET_TTL, cancellationToken);
                return Redirect(FrontendResultUrl($"ticket={ticket}"));
            }
            catch (AdminException ex) when (ex.Code == ErrorCode.OAuthAccountNotBound)
            {
                // pending-link:外部身份已解析,但未绑本地账号。票据进 URL 可被复制;
                // 再下发仅本浏览器持有的 binder cookie,claim 时必须同浏览器,挡钓鱼抢绑。
                var pending = RandomToken();
                var pendingBinder = RandomToken();
                Response.Cookies.Append(PENDING_COOKIE, pendingBinder, PendingCookieOptions());
                await cache.SetAsync(CacheKeys.OAuthPendingLink(pending), new ExternalPendingLinkPayload
                {
                    Provider = identity.Provider,
                    Subject = identity.Subject,
                    DisplayName = identity.DisplayName,
                    Email = identity.Email,
                    Phone = identity.Phone,
                    Binder = pendingBinder,
                }, PENDING_LINK_TTL, cancellationToken);
                var q = $"pendingLink={Uri.EscapeDataString(pending)}&provider={Uri.EscapeDataString(provider)}";
                // displayName 非敏感,仅供登录页确认框展示;subject 仍只在服务端缓存
                if (!string.IsNullOrWhiteSpace(identity.DisplayName))
                    q += $"&displayName={Uri.EscapeDataString(identity.DisplayName)}";
                return Redirect(FrontendResultUrl(q));
            }
        }
        catch (AdminException ex)
        {
            // TOTP 二次验证信令(40018):必须把 challengeId 带回前端,否则 SSO 合法路径无法完成登录
            if (ex.Code == ErrorCode.TotpRequired
                && ex.Args is not null
                && ex.Args.TryGetValue("challengeId", out var cid)
                && cid is string challengeId
                && !string.IsNullOrEmpty(challengeId))
            {
                return Redirect(FrontendResultUrl(
                    $"totpChallenge={Uri.EscapeDataString(challengeId)}&error={(int)ErrorCode.TotpRequired}"));
            }

            // 登录/绑定失败也回前端(错误码在 URL,前端按码查文案);避免把裸异常页甩给用户
            return Redirect(FrontendResultUrl($"error={(int)ex.Code}"));
        }
    }

    /// <summary>一次性票据换令牌(登录回调后前端调用;票据无效/过期/已用抛 40014)。Level3 同步写 Cookie/CSRF。</summary>
    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<Result<LoginOutput>> Exchange(ExternalExchangeInput input, CancellationToken cancellationToken)
    {
        var output = await cache.GetAndRemoveAsync<LoginOutput>(CacheKeys.OAuthTicket(input.Ticket), cancellationToken);
        AdminException.ThrowIf(output is null, ErrorCode.OAuthStateInvalid);
        return Result<LoginOutput>.Ok(cookies.ApplyAuthCookies(HttpContext, output!));
    }

    /// <summary>
    /// 认领「未绑定外部登录」待绑定票据:账密/短信登录成功后前端调用,把已解析的外部身份绑到当前用户。
    /// 票据无效/过期/已用/非发起浏览器(缺 binder cookie) → 40014;已被他人绑定 → 40017。
    /// </summary>
    [HttpPost("pending-link/claim")]
    [ActiveSession]
    [OperationLog("认领外部登录待绑定")]
    public async Task<Result<bool>> ClaimPendingLink(ExternalClaimPendingLinkInput input, CancellationToken cancellationToken)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.PendingLink), ErrorCode.OAuthStateInvalid);
        var key = CacheKeys.OAuthPendingLink(input.PendingLink.Trim());

        // 先读不消费:binder 不对时保留票据,合法浏览器仍可重试(勿因钓鱼试探把真用户票据烧掉)
        var peek = await cache.GetAsync<ExternalPendingLinkPayload>(key, cancellationToken);
        if (peek is null || string.IsNullOrEmpty(peek.Provider) || string.IsNullOrEmpty(peek.Subject))
            throw new AdminException(ErrorCode.OAuthStateInvalid);

        // 同浏览器门:cookie 必须与签发时写入 payload 的 binder 一致(防换浏览器抢绑 / 钓鱼链接)
        var cookieBinder = Request.Cookies[PENDING_COOKIE];
        if (string.IsNullOrEmpty(peek.Binder)
            || string.IsNullOrEmpty(cookieBinder)
            || !FixedTimeEquals(cookieBinder, peek.Binder))
            throw new AdminException(ErrorCode.OAuthStateInvalid);

        // binder 通过后再单次消费
        var payload = await cache.GetAndRemoveAsync<ExternalPendingLinkPayload>(key, cancellationToken);
        if (payload is null || string.IsNullOrEmpty(payload.Provider) || string.IsNullOrEmpty(payload.Subject))
            throw new AdminException(ErrorCode.OAuthStateInvalid);
        Response.Cookies.Delete(PENDING_COOKIE, new CookieOptions { Path = "/api/v1/auth/external" });

        // 运营可能在窗口内关掉该 provider:与 bind 回调一致,fail-closed
        await ResolveEnabledProviderAsync(payload.Provider);

        var identity = new ExternalIdentity(
            payload.Provider,
            payload.Subject,
            payload.DisplayName,
            payload.Email,
            payload.Phone);
        await externalBindings.BindAsync(CurrentUserId, identity);
        return Result<bool>.Ok(true);
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

        // 登录 CSRF 防御:把 state 绑定到发起浏览器——下发仅本浏览器持有的 binder cookie,回调时比对。
        // bind 模式免此(目标用户已由 [ActiveSession] 锁定)。ponytail: 同源同名 cookie,多标签并发 SSO 登录后发起者会互相覆盖,末发起者胜;可接受。
        var binder = "";
        if (mode != "bind")
        {
            binder = RandomToken();
            Response.Cookies.Append(STATE_COOKIE, binder, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,        // 生产 https 加 Secure;dev http(localhost)放行,否则本地登不了
                SameSite = SameSiteMode.Lax,     // Lax:放行 IdP 跨站顶层导航回调携带(Strict 会拦掉 → 登录必失败)
                MaxAge = STATE_TTL,
                Path = "/api/v1/auth/external",
            });
        }

        await cache.SetAsync(CacheKeys.OAuthState(state), new ExternalOAuthState
        {
            Mode = mode,
            ProviderCode = provider.Code,
            Nonce = nonce,
            CodeVerifier = verifier,
            RedirectUri = redirectUri,
            UserId = userId,
            Binder = binder,
        }, STATE_TTL, cancellationToken);

        return await provider.BuildAuthorizeUrlAsync(
            new ExternalAuthorizeRequest(state, nonce, CodeChallenge(verifier), redirectUri), cancellationToken);
    }

    /// <summary>绑定回调:换外部身份 → 绑到 state 记录的当前用户(已被他人占用抛 40017)。</summary>
    protected virtual async Task HandleBindCallbackAsync(ExternalOAuthState st, string code, CancellationToken cancellationToken)
    {
        // 复用登录同款"存在+启用"校验(provider 在 5min 授权窗口内被运营 kill-switch 关掉则拒,与登录路一致)
        var provider = await ResolveEnabledProviderAsync(st.ProviderCode);
        var identity = await provider.ExchangeAsync(
            new ExternalExchangeRequest(code, st.CodeVerifier, st.Nonce, st.RedirectUri), cancellationToken);
        await externalBindings.BindAsync(st.UserId!.Value, identity);
    }

    /// <summary>回调地址:配了 <c>CallbackBaseUrl</c> 用之(生产必配),否则仅开发环境回退到请求主机。</summary>
    protected virtual string CallbackUri(string provider)
    {
        if (string.IsNullOrWhiteSpace(options.CallbackBaseUrl))
        {
            // 生产必须显式配 CallbackBaseUrl:靠请求 Host 头推 redirect_uri 可被伪造带偏(OAuth mix-up 面)。
            // 开发环境放行回退到请求主机,免本地配置负担(同 JWT 开发密钥:dev 宽松、prod fail-fast)。
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "外部登录已启用,但生产环境未配置 TenonAdmin:ExternalAuth:CallbackBaseUrl。" +
                    "回调基址(redirect_uri 来源)必须是稳定的后端公网地址,不能从请求 Host 头推断(可伪造)。");
            return $"{Request.Scheme}://{Request.Host}/api/v1/auth/external/{provider}/callback";
        }
        return $"{options.CallbackBaseUrl!.TrimEnd('/')}/api/v1/auth/external/{provider}/callback";
    }

    /// <summary>前端结果页 URL(相对路径=同源部署,绝对 URL=前后端分离 dev);追加查询参数。</summary>
    protected virtual string FrontendResultUrl(string query)
    {
        var basePath = options.FrontendResultPath;
        var sep = basePath.Contains('?') ? '&' : '?';
        return $"{basePath}{sep}{query}";
    }

    /// <summary>pending-link binder cookie:仅本浏览器 claim 时携带;Path 与 API 前缀对齐。</summary>
    private CookieOptions PendingCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = PENDING_LINK_TTL,
        Path = "/api/v1/auth/external",
    };

    /// <summary>256 位加密随机 → base64url(state/nonce/PKCE verifier/票据/binder 通用)。</summary>
    protected static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>定长防时序侧信道比较 binder(等长即比对内容,不等长直接 false;base64url 为 ASCII)。</summary>
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>PKCE code_challenge = base64url(SHA256(verifier))(S256)。</summary>
    protected static string CodeChallenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
