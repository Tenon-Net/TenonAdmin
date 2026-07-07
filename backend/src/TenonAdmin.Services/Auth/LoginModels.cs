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
}
