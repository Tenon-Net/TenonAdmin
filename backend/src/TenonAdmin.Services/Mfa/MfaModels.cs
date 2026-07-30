namespace TenonAdmin.Services;

/// <summary>管理员发放 TOTP 绑定邀请出参(令牌仅此一次返回)。</summary>
public record TotpBindInviteOutput
{
    /// <summary>一次性邀请 bearer 明文(服务端只存哈希)</summary>
    public string Token { get; init; } = "";

    /// <summary>目标用户 Id</summary>
    public long UserId { get; init; }

    /// <summary>过期时刻(本地时钟口径)</summary>
    public DateTime ExpiresAt { get; init; }
}

/// <summary>绑定启动入参:邀请(或 InitGrant)+ 当前密码 → 下发 otpauth URI。</summary>
public record TotpBindStartInput
{
    /// <summary>绑定邀请 token 或部署 InitGrant</summary>
    public string Token { get; init; } = "";

    /// <summary>目标用户当前密码(必填;缺失/错误一律拒绝写 seed)</summary>
    public string CurrentPassword { get; init; } = "";

    /// <summary>
    /// 可选:显式目标用户账号(InitGrant 路径需要指定要绑定的超管账号;
    /// 邀请路径忽略,以邀请记录中的 UserId 为准)。
    /// </summary>
    public string? Account { get; init; }
}

/// <summary>绑定启动出参:临时挑战 + otpauth URI(种子暂存缓存,完成前不落库)。</summary>
public record TotpBindStartOutput
{
    /// <summary>绑定挑战 Id(完成绑定时回传)</summary>
    public string BindChallengeId { get; init; } = "";

    /// <summary>otpauth URI,供扫码</summary>
    public string OtpauthUri { get; init; } = "";

    /// <summary>Base32 种子明文(仅此响应;便于无法扫码时手输;服务端不落库明文)</summary>
    public string Seed { get; init; } = "";

    /// <summary>挑战过期秒数</summary>
    public int ExpiresSeconds { get; init; }
}

/// <summary>绑定完成入参:挑战 + 当前 Authenticator 动态口令。</summary>
public record TotpBindCompleteInput
{
    /// <summary><see cref="TotpBindStartOutput.BindChallengeId"/></summary>
    public string BindChallengeId { get; init; } = "";

    /// <summary>6 位 TOTP 动态口令(证明用户已正确配置认证器)</summary>
    public string TotpCode { get; init; } = "";
}

/// <summary>绑定完成出参:10 个恢复码仅展示一次。</summary>
public record TotpBindCompleteOutput
{
    /// <summary>10 个一次性恢复码明文(服务端只存哈希)</summary>
    public IReadOnlyList<string> RecoveryCodes { get; init; } = Array.Empty<string>();
}

/// <summary>使用恢复码入参。</summary>
public record TotpRecoveryInput
{
    /// <summary>登录账号</summary>
    public string Account { get; init; } = "";

    /// <summary>当前密码(防止仅持有恢复码的旁路)</summary>
    public string CurrentPassword { get; init; } = "";

    /// <summary>一次性恢复码</summary>
    public string RecoveryCode { get; init; } = "";
}

/// <summary>超级管理员 MFA 重置入参。</summary>
public record TotpSuperAdminResetInput
{
    /// <summary>被重置的超管用户 Id</summary>
    public long TargetUserId { get; init; }

    /// <summary>
    /// 批准方式:<c>peer</c>=另一已启用 TOTP 的超管操作(需当前操作者已 reauth);
    /// <c>emergency</c>=部署紧急授权(仅唯一超管场景)。
    /// </summary>
    public string Mode { get; init; } = "peer";

    /// <summary>紧急授权明文(<c>Mode=emergency</c> 时必填)</summary>
    public string? EmergencyGrant { get; init; }
}

/// <summary>重置后发放的重新绑定邀请(不直接解除后无邀请——返回新邀请 token)。</summary>
public record TotpResetOutput
{
    /// <summary>重新绑定邀请(与管理员邀请同形)</summary>
    public TotpBindInviteOutput Invite { get; init; } = new();
}

/// <summary>匿名紧急恢复入参:唯一超管丢失 TOTP 且无有效会话时使用。</summary>
public record TotpEmergencyResetInput
{
    /// <summary>部署 EmergencyGrant 明文</summary>
    public string EmergencyGrant { get; init; } = "";

    /// <summary>超管账号</summary>
    public string Account { get; init; } = "";

    /// <summary>当前密码(证明账户控制权)</summary>
    public string CurrentPassword { get; init; } = "";
}

/// <summary>再次认证入参。</summary>
public record ReauthInput
{
    /// <summary>方法:<c>totp</c> | <c>password</c></summary>
    public string Method { get; init; } = "totp";

    /// <summary>TOTP 动态口令(method=totp)</summary>
    public string? TotpCode { get; init; }

    /// <summary>当前密码(method=password)</summary>
    public string? Password { get; init; }
}
