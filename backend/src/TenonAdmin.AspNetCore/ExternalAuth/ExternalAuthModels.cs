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
}

/// <summary>登录页可用的外部登录方式(仅非密钥字段;点亮 SSO 按钮用)。</summary>
public record ExternalProviderItem
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public string? Icon { get; init; }
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

/// <summary>发起绑定的出参:前端拿到授权 URL 后自行跳转开始 OAuth 往返。</summary>
public record ExternalBindStartOutput
{
    public required string AuthorizeUrl { get; init; }
}
