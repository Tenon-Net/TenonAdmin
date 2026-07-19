namespace TenonAdmin.Services;

/// <summary>登录入参。验证码字段在 <c>Security:Captcha:Enabled</c> 关闭时可不传(默认关)。</summary>
public record LoginInput
{
    /// <summary>登录账号</summary>
    public string Account { get; init; } = "";

    /// <summary>明文密码(仅存在于请求生命周期,服务端只比对哈希、绝不落盘)</summary>
    public string Password { get; init; } = "";

    /// <summary>验证码票据 Id(取自 <c>GET /auth/captcha</c>;验证码启用时必传)</summary>
    public string? CaptchaId { get; init; }

    /// <summary>用户输入的验证码(验证码启用时必传)</summary>
    public string? CaptchaCode { get; init; }
}

/// <summary>刷新令牌换发入参</summary>
public record RefreshInput
{
    /// <summary>登录时下发的刷新令牌明文(服务端比对其哈希)</summary>
    public string RefreshToken { get; init; } = "";
}

/// <summary>短信二次验证完成登录入参(密码登录抛 40009 信令后,凭挑战 Id + 短信码换令牌)</summary>
public record SmsChallengeLoginInput
{
    /// <summary>挑战票据 Id(取自 40009 信令的 args.challengeId)</summary>
    public string ChallengeId { get; init; } = "";

    /// <summary>用户收到的短信验证码</summary>
    public string Code { get; init; } = "";
}

/// <summary>短信二次验证重发入参(持有活挑战即已过密码校验,无需图形验证码)</summary>
public record SmsResendInput
{
    /// <summary>挑战票据 Id</summary>
    public string ChallengeId { get; init; } = "";
}

/// <summary>免密登录发码入参。图形验证码启用时必传(发码端点防脚本滥用)。</summary>
public record PhoneCodeInput
{
    /// <summary>手机号</summary>
    public string Phone { get; init; } = "";

    /// <summary>验证码票据 Id(图形验证码启用时必传)</summary>
    public string? CaptchaId { get; init; }

    /// <summary>用户输入的图形验证码(启用时必传)</summary>
    public string? CaptchaCode { get; init; }
}

/// <summary>免密登录入参(手机号 + 短信码直接换令牌)</summary>
public record PhoneLoginInput
{
    /// <summary>手机号(与发码时一致)</summary>
    public string Phone { get; init; } = "";

    /// <summary>用户收到的短信验证码</summary>
    public string Code { get; init; } = "";
}

/// <summary>
/// 外部登录(OAuth/SSO 回调)入参:回调阶段已由 state 从缓存还原的换取参数 + provider 码。
/// 控制器构造,<see cref="IAuthService.LoginByExternalAsync"/> 消费(令牌不进重定向 URL,故这里是服务端内部传参)。
/// </summary>
public record ExternalLoginInput
{
    /// <summary>provider 码(选中哪个 <c>IExternalAuthProvider</c>)</summary>
    public string ProviderCode { get; init; } = "";

    /// <summary>IdP 回调带回的授权码</summary>
    public string Code { get; init; } = "";

    /// <summary>PKCE 校验器(授权阶段留存;非 PKCE provider 忽略)</summary>
    public string CodeVerifier { get; init; } = "";

    /// <summary>授权阶段的 nonce(供 OIDC 校验 id_token 的 nonce 声明)</summary>
    public string Nonce { get; init; } = "";

    /// <summary>回调地址(须与授权阶段一致)</summary>
    public string RedirectUri { get; init; } = "";
}

/// <summary>发码出参:有效期与重发间隔(驱动前端倒计时;刻意不含任何用户信息,防枚举)</summary>
public record SmsSendOutput
{
    /// <summary>验证码有效期(秒)</summary>
    public int ExpiresSeconds { get; init; }

    /// <summary>重发最小间隔(秒)</summary>
    public int ResendSeconds { get; init; }
}

/// <summary>登录出参:令牌对 + 基础用户信息(前端存令牌、显示用户名所需的最小集)</summary>
public record LoginOutput
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset RefreshExpiresAt { get; init; }
    public required long UserId { get; init; }
    public required string Account { get; init; }
    public required string Name { get; init; }

    /// <summary>是否需强制修改密码(管理员建号/重置后首登为 true);前端据此强制跳转改密页,后端不拦登录。</summary>
    public bool MustChangePassword { get; init; }
}
