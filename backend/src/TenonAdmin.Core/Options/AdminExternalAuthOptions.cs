namespace TenonAdmin.Core;

/// <summary>
/// 外部登录 / SSO 配置(对应 <c>TenonAdmin:ExternalAuth</c> 节,设计批次 D)。
/// <para><b>配置形态取"折中"</b>:连接与密钥(此处的 <see cref="Oidc"/> 条目、卫星包的 corpSecret/appSecret)走 appsettings /
/// 密钥管理器,<b>不进库</b>;而运营项(启用开关、未绑定策略、开户默认角色/机构)走 <c>sys_config</c>
/// 键 <c>sys.externalauth.{code}.{enabled|provisioning|defaultRoleIds|defaultOrgId}</c>(运行时可改、无需重启)。</para>
/// <para>企业微信 / 钉钉等厂商 provider 由卫星包各自读取自有配置节(如 <c>TenonAdmin:ExternalAuth:WeCom</c>),
/// 不在本类,以免核心 Options 沾厂商专有字段。</para>
/// </summary>
public class AdminExternalAuthOptions
{
    /// <summary>
    /// 后端对外可达的公网基址(拼 <c>redirect_uri</c> 用,须与 IdP 注册的回调前缀一致,如 <c>https://admin.example.com</c>)。
    /// <para>为空则从当前请求推断(<c>scheme://host</c>);反向代理下请显式配,否则推断出内网地址致回调对不上。</para>
    /// </summary>
    public string? CallbackBaseUrl { get; set; }

    /// <summary>
    /// 登录成功后带一次性票据重定向到的<b>前端</b>页路径(SPA 在此页用 ticket 调 <c>POST /auth/external/exchange</c> 换令牌)。
    /// 默认 <c>/oauth/callback</c>。令牌<b>不进</b>重定向 URL,只带票据(见 <see cref="CacheKeys.OAuthTicket"/>)。
    /// </summary>
    public string FrontendResultPath { get; set; } = "/oauth/callback";

    /// <summary>
    /// 内置 OIDC provider 配置项:每项一个可登录的标准 OIDC IdP(Keycloak/Entra/Authing/Auth0…)。
    /// 为空则不注册任何内置 OIDC provider(登录页也就不显 SSO 按钮)。
    /// </summary>
    public List<OidcProviderOptions> Oidc { get; set; } = new();
}

/// <summary>单个内置 OIDC provider 的连接配置(设计批次 D;密钥随部署,不进库)。</summary>
public class OidcProviderOptions
{
    /// <summary>provider 唯一码(登录按钮 / 回调路由 / 运营配置键都用它;多 IdP 时须互不相同,如 <c>keycloak</c>、<c>entra</c>)</summary>
    public string Code { get; set; } = "oidc";

    /// <summary>展示名(登录页按钮文案兜底;前端可用 i18n 覆盖)</summary>
    public string DisplayName { get; set; } = "SSO";

    /// <summary>图标(iconify 名或图片 URL,可空)</summary>
    public string? Icon { get; set; }

    /// <summary>OIDC 颁发者基址(取 <c>{Authority}/.well-known/openid-configuration</c> 发现文档,拿 authorization/token 端点与 JWKS)</summary>
    public string Authority { get; set; } = "";

    /// <summary>客户端 Id(IdP 注册的 client_id)</summary>
    public string ClientId { get; set; } = "";

    /// <summary>客户端密钥(机密客户端;全程服务端持有,不下发浏览器)</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>申请的 scope(空格分隔,默认 <c>openid profile email</c>;<c>openid</c> 必带)</summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>是否启用 PKCE(S256,默认启用;机密客户端也建议开,防授权码注入)</summary>
    public bool UsePkce { get; set; } = true;
}
