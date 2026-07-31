namespace TenonAdmin.Core;

/// <summary>会话并发模式(对应 <c>TenonAdmin:Security:Session:Mode</c>)。</summary>
public enum SessionMode
{
    /// <summary>多端并存(默认):同一用户可有多个活跃会话</summary>
    Multi,

    /// <summary>单端:新登录吊销该用户其他所有会话(挤下线)</summary>
    Single,
}

/// <summary>
/// 历史安全档键(对应 <c>TenonAdmin:Security:Profile</c>)。
/// <para><b>ADR 0006：完整 Level3 不再是产品目标。</b>保留枚举以免破坏既有配置反序列化；
/// 新代码应使用 <see cref="AdminTotpOptions"/> / <see cref="AdminSessionOptions.CookieMode"/> 等独立键，
/// 仅过渡期可经 <see cref="AdminSecurityOptions.IsTotpFeatureEnabled"/> 等 helper 兼容旧 <see cref="Level3"/>。</para>
/// </summary>
public enum SecurityProfile
{
    /// <summary>默认:无历史 Level3 总档语义</summary>
    None = 0,

    /// <summary>历史 Level3 总档(废弃产品路径;过渡期等同打开多项可选能力)。</summary>
    Level3 = 1,
}

/// <summary>
/// 数据保护密钥(对应 <c>TenonAdmin:Security:DataProtection</c>)。
/// 为 <c>ISecretProtector</c> 提供主密钥材料;可替换为 KMS 的 <c>IDataProtectionKeyProvider</c>。
/// </summary>
public class AdminDataProtectionOptions
{
    /// <summary>
    /// 主密钥 Base64(解码后建议 ≥32 字节)。null 时开发环境可自动生成落盘密钥;
    /// 生产启用 TOTP/Cookie 等涉密能力时建议显式配置。
    /// </summary>
    public string? Key { get; set; }

    /// <summary>当前密钥版本号(轮换时递增;信封带版本以便渐进解密)</summary>
    public int KeyVersion { get; set; } = 1;
}

/// <summary>
/// TOTP 二因子(对应 <c>TenonAdmin:Security:Totp</c>)。默认全关;显式 <see cref="Enabled"/> 后才提供绑定/挑战。
/// 绑定模型:用户自助(ADR 0006);恢复码在绑定时下发。
/// </summary>
public class AdminTotpOptions
{
    /// <summary>是否启用 TOTP 能力(绑定 API、登录挑战、恢复码)。默认 <c>false</c>。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 启用 TOTP 时,超级管理员是否必须完成第二因子(未绑定则登录引导自助绑定,不走部署期 InitGrant)。
    /// 默认 <c>false</c>——由账号 <c>ForceTotp</c> 或用户自愿绑定控制。
    /// </summary>
    public bool RequireForSuperAdmin { get; set; }

    /// <summary>otpauth URI 中的 issuer(Authenticator 展示名);默认 TenonAdmin。</summary>
    public string Issuer { get; set; } = "TenonAdmin";

    /// <summary>登录/提权 TOTP 挑战有效期(秒);默认 300。</summary>
    public int ChallengeTtlSeconds { get; set; } = 300;

    /// <summary>高危操作再次确认有效窗口(分钟);默认 5。</summary>
    public int ReauthWindowMinutes { get; set; } = 5;

    /// <summary>每次绑定生成的恢复码个数;默认 10。</summary>
    public int RecoveryCodeCount { get; set; } = 10;
}

/// <summary>
/// 历史 Level3 节(对应 <c>TenonAdmin:Security:Level3</c>)。
/// <b>ADR 0006：非产品路径</b>。InitGrant/Emergency/邀请等仪式键已拆除实现;
/// 仍保留 CookieDomain 与 TOTP 参数字段,供旧配置反序列化与 helper 回退。
/// </summary>
public class AdminLevel3Options
{
    /// <summary>已迁到 <c>Security:Session:CookieDomain</c>;此处仅旧配置回退。</summary>
    public string? CookieDomain { get; set; }

    public int ReauthWindowMinutes { get; set; } = 5;
    public int TotpChallengeTtlSeconds { get; set; } = 300;
    public string TotpIssuer { get; set; } = "TenonAdmin";
}

/// <summary>会话配置(对应 <c>TenonAdmin:Security:Session</c>)。</summary>
public class AdminSessionOptions
{
    /// <summary>
    /// 是否使用 Cookie 会话模式:refresh → HttpOnly Cookie,access → 仅内存,并启用双提交 CSRF。
    /// 默认 <c>false</c> = body/localStorage 兼容模式。
    /// 配置键:<c>TenonAdmin:Security:Session:CookieMode</c>。
    /// </summary>
    public bool CookieMode { get; set; }

    /// <summary>
    /// Cookie Domain(如 <c>.example.com</c>)。空 = 当前 host(推荐同源反代)。
    /// 跨源 SPA+API 时须显式设置,并配合 CORS 凭证。
    /// 配置键:<c>TenonAdmin:Security:Session:CookieDomain</c>。
    /// </summary>
    public string? CookieDomain { get; set; }

    /// <summary>并发模式:Multi(默认)| Single(新登录踢旧)</summary>
    public SessionMode Mode { get; set; } = SessionMode.Multi;

    /// <summary>最大并发会话数;&gt;0 时超出则吊销最旧。0 = 不限(默认)。</summary>
    public int MaxConcurrent { get; set; }

    /// <summary>
    /// 会话活动回写节流(秒):热路径更新缓存,满间隔再写 DB。默认 60。
    /// 仅当启用闲置超时(<see cref="IdleMinutesNormal"/> &gt; 0)时有意义。
    /// </summary>
    public int ActivityThrottleSeconds { get; set; } = 60;

    /// <summary>
    /// 普通用户闲置超时(分钟)。<b>0 = 不启用闲置过期</b>(默认,零配置不杀会话)。
    /// 配置键:<c>TenonAdmin:Security:Session:IdleMinutesNormal</c>。
    /// </summary>
    public int IdleMinutesNormal { get; set; }

    /// <summary>
    /// 已启用 TOTP 的用户闲置超时(分钟)。0 = 与 <see cref="IdleMinutesNormal"/> 相同。
    /// 配置键:<c>TenonAdmin:Security:Session:IdleMinutesMfa</c>。
    /// </summary>
    public int IdleMinutesMfa { get; set; }

    /// <summary>
    /// 绝对会话最长寿命(小时)。0 = 不额外限制(默认,仅随 refresh 过期)。
    /// 配置键:<c>TenonAdmin:Security:Session:AbsoluteHours</c>。
    /// </summary>
    public int AbsoluteHours { get; set; }
}

/// <summary>验证码配置(对应 <c>TenonAdmin:Security:Captcha</c>)。</summary>
public class AdminCaptchaOptions
{
    /// <summary>是否启用登录验证码。默认关。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 验证码类型:<c>char</c>(默认)| <c>path</c>| <c>math</c>。
    /// 运行时可以 DB 键 <c>sys.security.captcha.type</c> 覆盖。
    /// </summary>
    public string Type { get; set; } = "char";
}

/// <summary>登录失败锁定(对应 <c>TenonAdmin:Security:LoginLock</c>)。</summary>
public class AdminLoginLockOptions
{
    /// <summary>连续密码错误多少次后锁定;&lt;=0 表示关闭。</summary>
    public int MaxFailCount { get; set; } = 5;

    /// <summary>锁定时长(分钟);也是失败计数滑动窗口。</summary>
    public int LockMinutes { get; set; } = 10;
}

/// <summary>请求限流(对应 <c>TenonAdmin:Security:RateLimit</c>)。</summary>
public class AdminRateLimitOptions
{
    public const string KEY_ENABLED = "sys.security.rateLimit.enabled";
    public const string KEY_WINDOW = "sys.security.rateLimit.windowSeconds";
    public const string KEY_PERMIT = "sys.security.rateLimit.permitPerWindow";
    public const string KEY_AUTH_PERMIT = "sys.security.rateLimit.authPermitPerWindow";

    /// <summary>部署期硬总开关;false 时无论 DB 如何都不限流。默认 true。</summary>
    public bool Enabled { get; set; } = true;

    public int WindowSeconds { get; set; } = 60;
    public int PermitPerWindow { get; set; } = 300;
    public int AuthPermitPerWindow { get; set; } = 20;
}

/// <summary>短信验证码(对应 <c>TenonAdmin:Security:SmsOtp</c>)。与 TOTP 独立。</summary>
public class AdminSmsOtpOptions
{
    /// <summary>短信二次验证兜底开关(默认关)</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>短信免密登录兜底开关(默认关)</summary>
    public bool LoginEnabled { get; set; }

    public int CodeLength { get; set; } = 6;
    public int TtlSeconds { get; set; } = 300;
    public int ResendSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public int DailySendLimitPerPhone { get; set; } = 10;
}

/// <summary>
/// 安全配置根节(对应 <c>TenonAdmin:Security</c>)。
/// 产品形状(ADR 0006):独立可选键,默认宽松;见仓库 <c>docs/agents/security-optional-config.md</c>。
/// </summary>
public class AdminSecurityOptions
{
    /// <summary>
    /// 历史总档。默认 <see cref="SecurityProfile.None"/>。
    /// 请改用 <see cref="Totp"/> / <see cref="Session"/>;过渡期 helper 仍认 <see cref="SecurityProfile.Level3"/>。
    /// </summary>
    public SecurityProfile Profile { get; set; } = SecurityProfile.None;

    /// <summary>TOTP 二因子(默认关)</summary>
    public AdminTotpOptions Totp { get; set; } = new();

    /// <summary>会话:Cookie 模式 / 并发 / 闲置 / 绝对寿命</summary>
    public AdminSessionOptions Session { get; set; } = new();

    public AdminLoginLockOptions LoginLock { get; set; } = new();
    public AdminCaptchaOptions Captcha { get; set; } = new();
    public AdminRateLimitOptions RateLimit { get; set; } = new();
    public AdminSmsOtpOptions SmsOtp { get; set; } = new();
    public AdminDataProtectionOptions DataProtection { get; set; } = new();

    /// <summary>历史 Level3 授权块;勿新写。产品键见 <see cref="Totp"/> / <see cref="Session"/>。</summary>
    public AdminLevel3Options Level3 { get; set; } = new();

    /// <summary>
    /// 新建/重置未显式给密时的默认初始口令。null = 密码学随机强口令(推荐)。
    /// </summary>
    public string? DefaultInitialPassword { get; set; }

    // ── 有效能力判定(产品键优先;Profile=Level3 仅过渡兼容) ──

    /// <summary>
    /// 是否仍配置了历史 <c>Profile=Level3</c> 总档。
    /// 仅用于预检/闲置账号 Job/策略地板等过渡逻辑;新产品代码请用 <see cref="IsTotpFeatureEnabled"/> 等。
    /// </summary>
    public bool IsLegacyLevel3Profile => Profile == SecurityProfile.Level3;

    /// <summary>TOTP 功能是否可用: <c>Totp:Enabled</c> 或历史 Profile=Level3。</summary>
    public bool IsTotpFeatureEnabled =>
        Totp.Enabled || IsLegacyLevel3Profile;

    /// <summary>Cookie+CSRF 会话是否启用: <c>Session:CookieMode</c> 或历史 Profile=Level3。</summary>
    public bool IsCookieSessionEnabled =>
        Session.CookieMode || IsLegacyLevel3Profile;

    /// <summary>是否启用闲置会话过期(任一侧 &gt; 0,或历史 Level3 使用内置下限)。</summary>
    public bool IsSessionIdleEnabled =>
        Session.IdleMinutesNormal > 0
        || Session.IdleMinutesMfa > 0
        || IsLegacyLevel3Profile;

    /// <summary>是否启用绝对会话寿命(<c>AbsoluteHours</c> &gt; 0,或历史 Level3 8h 下限)。</summary>
    public bool IsSessionAbsoluteEnabled =>
        Session.AbsoluteHours > 0 || IsLegacyLevel3Profile;

    /// <summary>
    /// 解析 Cookie Domain:优先 <c>Session:CookieDomain</c>,否则回退历史 <c>Level3:CookieDomain</c>。
    /// </summary>
    public string? ResolveCookieDomain()
    {
        if (!string.IsNullOrWhiteSpace(Session.CookieDomain))
            return Session.CookieDomain.Trim();
        var legacy = Level3.CookieDomain;
        return string.IsNullOrWhiteSpace(legacy) ? null : legacy.Trim();
    }

    /// <summary>解析 TOTP issuer。</summary>
    public string ResolveTotpIssuer()
    {
        if (!string.IsNullOrWhiteSpace(Totp.Issuer))
            return Totp.Issuer.Trim();
        return string.IsNullOrWhiteSpace(Level3.TotpIssuer) ? "TenonAdmin" : Level3.TotpIssuer.Trim();
    }

    /// <summary>解析 TOTP 挑战 TTL(秒)。</summary>
    public int ResolveTotpChallengeTtlSeconds()
    {
        if (Totp.ChallengeTtlSeconds > 0) return Totp.ChallengeTtlSeconds;
        return Level3.TotpChallengeTtlSeconds > 0 ? Level3.TotpChallengeTtlSeconds : 300;
    }

    /// <summary>解析再认证窗口(分钟)。</summary>
    public int ResolveReauthWindowMinutes()
    {
        if (Totp.ReauthWindowMinutes > 0) return Totp.ReauthWindowMinutes;
        return Level3.ReauthWindowMinutes > 0 ? Level3.ReauthWindowMinutes : 5;
    }

    /// <summary>
    /// 解析闲置分钟数。<paramref name="mfaUser"/> 为 true 时优先 MFA 档;
    /// 历史 Level3 且配置为 0 时回落 15/30。
    /// </summary>
    public int ResolveIdleMinutes(bool mfaUser)
    {
        if (mfaUser && Session.IdleMinutesMfa > 0)
            return Session.IdleMinutesMfa;
        if (Session.IdleMinutesNormal > 0)
            return Session.IdleMinutesNormal;
        if (IsLegacyLevel3Profile)
            return mfaUser ? 15 : 30;
        return 0;
    }

    /// <summary>解析绝对会话寿命。历史 Level3 且未配置时为 8 小时。</summary>
    public TimeSpan? ResolveAbsoluteTimeSpan()
    {
        if (Session.AbsoluteHours > 0)
            return TimeSpan.FromHours(Session.AbsoluteHours);
        if (IsLegacyLevel3Profile)
            return TimeSpan.FromHours(8);
        return null;
    }
}
