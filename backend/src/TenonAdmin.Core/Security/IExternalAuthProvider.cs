namespace TenonAdmin.Core;

/// <summary>
/// 外部登录 / SSO 身份提供方(设计批次 D)。把"如何与某个 IdP 完成 OAuth2/OIDC 授权码交换"抽象成替换点:
/// 内核自带 <c>OidcExternalAuthProvider</c>(通吃 Keycloak/Entra/Authing/Auth0 等标准 OIDC);
/// 企业微信 / 钉钉等走卫星包(<c>TenonAdmin.Auth.WeCom</c> / <c>.DingTalk</c>),消费者亦可实现本接口接自有 IdP。
/// <para>注册用 <c>TryAddEnumerable</c>(可多 provider 并存),按 <see cref="Code"/> 选型;
/// 本接口只负责"外部身份解析",不碰本地账号/令牌——绑定与建会话在 <c>AuthService.LoginByExternalAsync</c>。</para>
/// <para>纪律:内核内置实现只依赖 SqlSugarCore + Microsoft.*(OIDC 复用 JwtBearer 已传递的
/// <c>Microsoft.IdentityModel.*</c>,零新包);厂商 SDK 一律下沉卫星包。</para>
/// </summary>
public interface IExternalAuthProvider
{
    /// <summary>provider 全局唯一码(登录按钮 / 回调路由 / 运营配置键都用它,如 <c>oidc</c>、<c>keycloak</c>、<c>wecom</c>)</summary>
    string Code { get; }

    /// <summary>展示名(登录页按钮文案兜底;前端可用 i18n 覆盖)</summary>
    string DisplayName { get; }

    /// <summary>图标(iconify 名或图片 URL,可空;前端登录按钮用)</summary>
    string? Icon { get; }

    /// <summary>
    /// 构造跳转到 IdP 的授权 URL(OIDC 需先取发现文档拿 authorization_endpoint,故为异步)。
    /// <paramref name="request"/> 里的 state 防 CSRF、nonce 防 id_token 重放、codeChallenge 为 PKCE(S256);
    /// 不支持 nonce/PKCE 的 provider 忽略即可。
    /// </summary>
    Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 用授权码换取外部身份:内部完成 token 端点交换 + 必要校验(OIDC 验 id_token 签名/issuer/audience/nonce),
    /// 返回 <see cref="ExternalIdentity"/>。校验不过应抛 <see cref="AdminException"/>(<see cref="ErrorCode.OAuthExchangeFailed"/>)。
    /// </summary>
    Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>授权阶段入参:调用方生成 state/nonce/PKCE 并服务端留存(见 <see cref="CacheKeys.OAuthState"/>),provider 据此拼授权 URL。</summary>
/// <param name="State">防 CSRF 的不透明态(回调时比对)</param>
/// <param name="Nonce">OIDC 防重放随机串(写进授权请求,回调时与 id_token 的 nonce 声明比对)</param>
/// <param name="CodeChallenge">PKCE code_challenge(S256;非 PKCE provider 忽略)</param>
/// <param name="RedirectUri">回调地址(须与交换阶段一致、且与 IdP 注册值一致)</param>
public sealed record ExternalAuthorizeRequest(string State, string Nonce, string CodeChallenge, string RedirectUri);

/// <summary>交换阶段入参:回调拿到授权码后,连同授权阶段留存的 nonce/PKCE 校验器一并交给 provider 换身份。</summary>
/// <param name="Code">IdP 回调带回的授权码</param>
/// <param name="CodeVerifier">PKCE code_verifier(与授权阶段 code_challenge 对应;非 PKCE provider 忽略)</param>
/// <param name="Nonce">授权阶段的 nonce(供 OIDC 校验 id_token 的 nonce 声明)</param>
/// <param name="RedirectUri">回调地址(须与授权阶段一致)</param>
public sealed record ExternalExchangeRequest(string Code, string CodeVerifier, string Nonce, string RedirectUri);

/// <summary>
/// 外部身份(provider 解析授权码后的结果)。<see cref="Provider"/> + <see cref="Subject"/> 唯一确定一个外部身份,
/// 即 <c>sys_user_external</c> 的绑定键;其余字段仅在自动开户时用于回填本地账号资料。
/// </summary>
/// <param name="Provider">provider 码(= 解析它的 <see cref="IExternalAuthProvider.Code"/>)</param>
/// <param name="Subject">外部唯一标识(OIDC 的 <c>sub</c> / 企业微信 userid / 钉钉 unionid)</param>
/// <param name="DisplayName">显示名(可空)</param>
/// <param name="Email">邮箱(可空)</param>
/// <param name="Phone">手机号(可空)</param>
public sealed record ExternalIdentity(
    string Provider,
    string Subject,
    string? DisplayName = null,
    string? Email = null,
    string? Phone = null);
