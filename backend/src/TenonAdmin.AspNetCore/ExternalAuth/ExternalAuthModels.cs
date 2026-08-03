namespace TenonAdmin.AspNetCore;

/// <summary>
/// 外部登录授权态(批次 D):授权阶段生成、存缓存(<see cref="Core.CacheKeys.OAuthState"/>),回调阶段一次性取出。
/// 承载 CSRF 态 + PKCE/nonce + 模式(login/bind)与绑定用户。<b>不入库、短 TTL</b>。
/// </summary>
public class ExternalOAuthState
{
    /// <summary><c>login</c>(登录换令牌)或 <c>bind</c>(个人中心绑定当前用户)</summary>
    public string Mode { get; set; } = "login";

    /// <summary>provider 码</summary>
    public string ProviderCode { get; set; } = "";

    /// <summary>OIDC nonce(回调校验 id_token 防重放)</summary>
    public string Nonce { get; set; } = "";

    /// <summary>PKCE code_verifier(交换阶段回传)</summary>
    public string CodeVerifier { get; set; } = "";

    /// <summary>回调地址(须与授权阶段一致)</summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>绑定模式下的当前登录用户 Id(登录模式为 null)</summary>
    public long? UserId { get; set; }

    /// <summary>
    /// 登录模式下绑定发起浏览器的随机 binder:授权阶段下发同值的 HttpOnly cookie,回调阶段比对,
    /// 防登录 CSRF(他人拼接的 code/state 诱导受害者登入攻击者账号)。bind 模式为空(已由 UserId 兜住)。
    /// </summary>
    public string Binder { get; set; } = "";
}

/// <summary>登录页可用的外部登录方式(仅非密钥字段;点亮 SSO 按钮用)。</summary>
public record ExternalProviderItem
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public string? Icon { get; init; }
}

/// <summary>管理端:全部已注册 provider + 当前运营启用状态(含已禁用,供配置中心开关)。</summary>
public record ExternalProviderAdminItem
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public string? Icon { get; init; }
    /// <summary>对应 <c>sys.externalauth.{code}.enabled</c>;缺省 true。</summary>
    public bool Enabled { get; init; }
}

/// <summary>个人中心"账号绑定"列表项(只回展示所需,不回 Subject 等原始标识)。</summary>
public record ExternalBindingItem
{
    public required string Provider { get; init; }
    public string? DisplayName { get; init; }
    public DateTime BoundAt { get; init; }
}

/// <summary>一次性票据换令牌入参(登录回调后前端凭票据拉取真正的令牌对)。</summary>
public record ExternalExchangeInput
{
    public string Ticket { get; init; } = "";
}

/// <summary>
/// 未绑定外部登录的待绑定缓存载荷(已解析身份,不再持有授权码)。
/// 键见 <see cref="Core.CacheKeys.OAuthPendingLink"/>。
/// <para><see cref="Binder"/> 与签发时下发的 HttpOnly cookie 对齐,防止换浏览器/钓鱼链接抢绑。</para>
/// </summary>
public class ExternalPendingLinkPayload
{
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>发起 OAuth 的浏览器 binder(与 <c>tn_oauth_pending</c> cookie 同值)。</summary>
    public string Binder { get; set; } = "";
}

/// <summary>账密登录后认领待绑定外部身份入参。</summary>
public record ExternalClaimPendingLinkInput
{
    public string PendingLink { get; init; } = "";
}

/// <summary>发起绑定的出参:前端拿到授权 URL 后自行跳转开始 OAuth 往返。</summary>
public record ExternalBindStartOutput
{
    public required string AuthorizeUrl { get; init; }
}
