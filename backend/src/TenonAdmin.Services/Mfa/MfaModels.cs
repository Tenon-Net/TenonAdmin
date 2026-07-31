namespace TenonAdmin.Services;

/// <summary>自助绑定启动入参:账号 + 当前密码 → otpauth URI。</summary>
public record TotpBindStartInput
{
    /// <summary>登录账号(自助绑定目标)</summary>
    public string Account { get; init; } = "";

    /// <summary>当前密码(必填;缺失/错误一律拒绝写 seed)</summary>
    public string CurrentPassword { get; init; } = "";

    /// <summary>
    /// 历史字段:曾表示邀请/InitGrant token。<b>ADR 0006 后忽略</b>;保留以免旧客户端反序列化失败。
    /// </summary>
    public string? Token { get; init; }
}

/// <summary>绑定启动出参:临时挑战 + otpauth URI(种子暂存缓存,完成前不落库)。</summary>
public record TotpBindStartOutput
{
    /// <summary>绑定挑战 Id(完成绑定时回传)</summary>
    public string BindChallengeId { get; init; } = "";

    /// <summary>otpauth URI,供扫码</summary>
    public string OtpauthUri { get; init; } = "";

    /// <summary>Base32 种子明文(仅此响应;服务端不落库明文)</summary>
    public string Seed { get; init; } = "";

    /// <summary>挑战过期秒数</summary>
    public int ExpiresSeconds { get; init; }
}

/// <summary>绑定完成入参:挑战 + 当前 Authenticator 动态口令。</summary>
public record TotpBindCompleteInput
{
    /// <summary><see cref="TotpBindStartOutput.BindChallengeId"/></summary>
    public string BindChallengeId { get; init; } = "";

    /// <summary>6 位 TOTP 动态口令</summary>
    public string TotpCode { get; init; } = "";
}

/// <summary>绑定完成出参:恢复码仅展示一次。</summary>
public record TotpBindCompleteOutput
{
    /// <summary>一次性恢复码明文(服务端只存哈希)</summary>
    public IReadOnlyList<string> RecoveryCodes { get; init; } = Array.Empty<string>();
}

/// <summary>使用恢复码入参。</summary>
public record TotpRecoveryInput
{
    public string Account { get; init; } = "";
    public string CurrentPassword { get; init; } = "";
    public string RecoveryCode { get; init; } = "";
}

/// <summary>管理员清除 MFA 入参。</summary>
public record TotpClearMfaInput
{
    public long UserId { get; init; }
}

/// <summary>再次认证入参。</summary>
public record ReauthInput
{
    /// <summary>方法:<c>totp</c> | <c>password</c></summary>
    public string Method { get; init; } = "totp";

    public string? TotpCode { get; init; }
    public string? Password { get; init; }
}
