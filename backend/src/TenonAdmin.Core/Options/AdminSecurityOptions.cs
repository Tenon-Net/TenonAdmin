namespace TenonAdmin.Core;

/// <summary>会话并发模式(设计 §15/§3.2 <c>Security:Session:Mode</c>)。</summary>
public enum SessionMode
{
    /// <summary>多端并存(默认):同一用户可有多个活跃会话</summary>
    Multi,

    /// <summary>单端:新登录吊销该用户其他所有会话(挤下线)</summary>
    Single,
}

/// <summary>
/// 安全档(对应 <c>TenonAdmin:Security:Profile</c>)。仅部署配置/密钥管理可信来源;不可经 SysConfig 或管理页降级。
/// 默认 <see cref="None"/>——既有项目升级零阻断;显式 <see cref="Level3"/> 才施加等保三级应用安全强制下限。
/// </summary>
public enum SecurityProfile
{
    /// <summary>默认档:不施加 Level3 下限,登录/刷新/localStorage 令牌模式完全兼容</summary>
    None = 0,

    /// <summary>等保三级应用安全强制档(显式启用)。内核不宣称「已通过等保三级」。</summary>
    Level3 = 1,
}

/// <summary>
/// 数据保护密钥配置(对应 <c>TenonAdmin:Security:DataProtection</c>)。
/// 为 <c>ISecretProtector</c> 提供主密钥材料;可替换为 KMS/HSM 的 <c>IDataProtectionKeyProvider</c>。
/// </summary>
public class AdminDataProtectionOptions
{
    /// <summary>
    /// 主密钥 Base64(解码后建议 ≥32 字节)。null 时开发环境可自动生成落盘密钥;
    /// Level3 必须显式配置(缺配由预检/启动失败)。
    /// </summary>
    public string? Key { get; set; }

    /// <summary>当前密钥版本号(轮换时递增;信封带版本以便渐进解密)</summary>
    public int KeyVersion { get; set; } = 1;
}

/// <summary>
/// Level3 MFA 相关部署配置(对应 <c>TenonAdmin:Security:Level3</c>)。
/// 初始化/紧急授权为部署期短时一次性 bearer;明文最多出现在部署密钥中,消费后由内核标记失效。
/// </summary>
public class AdminLevel3Options
{
    /// <summary>
    /// 首个超级管理员 TOTP 绑定用的部署期一次性初始化授权明文(高熵 bearer)。
    /// 绑定成功后立即失效;仅允许尚未绑定 TOTP 的超管使用。
    /// </summary>
    public string? InitGrant { get; set; }

    /// <summary>InitGrant 有效期(分钟);默认 60。从首次观测起算;超过后即使未消费也拒绝。</summary>
    public int InitGrantTtlMinutes { get; set; } = 60;

    /// <summary>
    /// InitGrant 绝对截止时刻(UTC,部署侧可验证)。Level3 尚无已绑定超管时<strong>必填</strong>;
    /// 缺失或已过期 → 预检 critical fail。与 <see cref="InitGrantTtlMinutes"/> 同时生效(取更严者)。
    /// </summary>
    public DateTimeOffset? InitGrantNotAfter { get; set; }

    /// <summary>
    /// 唯一超级管理员 MFA 紧急恢复授权明文。仅当系统中只有一名超管且需重置其 MFA 时使用;
    /// 消费后失效,并记最高级安全日志。
    /// </summary>
    public string? EmergencyGrant { get; set; }

    /// <summary>EmergencyGrant 有效期(分钟);默认 30。从首次观测起算。</summary>
    public int EmergencyGrantTtlMinutes { get; set; } = 30;

    /// <summary>EmergencyGrant 绝对截止时刻(UTC);过期拒绝,见 <see cref="InitGrantNotAfter"/>。</summary>
    public DateTimeOffset? EmergencyGrantNotAfter { get; set; }

    /// <summary>
    /// Level3 Cookie Domain(如 <c>.example.com</c>)。空 = 仅同源/同 host Cookie(推荐反代同源)。
    /// 跨源 SPA+API 时必须显式设置父域,并配合 CORS 凭证与预检。
    /// </summary>
    public string? CookieDomain { get; set; }

    /// <summary>TOTP 绑定邀请默认有效期(分钟);默认 15。</summary>
    public int BindInviteTtlMinutes { get; set; } = 15;

    /// <summary>高风险操作再次认证有效窗口(分钟);默认 5。</summary>
    public int ReauthWindowMinutes { get; set; } = 5;

    /// <summary>TOTP 登录挑战 TTL(秒);默认 300。</summary>
    public int TotpChallengeTtlSeconds { get; set; } = 300;

    /// <summary>TOTP otpauth URI 中的 issuer 名(Authenticator 展示);默认 TenonAdmin。</summary>
    public string TotpIssuer { get; set; } = "TenonAdmin";
}

/// <summary>会话配置(对应 <c>TenonAdmin:Security:Session</c>,设计 §15)。</summary>
public class AdminSessionOptions
{
    /// <summary>并发模式:Multi(默认)| Single(新登录踢旧)</summary>
    public SessionMode Mode { get; set; } = SessionMode.Multi;

    /// <summary>最大并发会话数;&gt;0 时超出则按最早登录吊销最旧会话。0 = 不限。</summary>
    public int MaxConcurrent { get; set; }

    /// <summary>
    /// Level3 活动回写节流秒数(热路径只更新缓存,满此间隔才回写 DB)。默认 60。
    /// 非 Level3 不启用闲置/绝对窗时本项无影响。
    /// </summary>
    public int ActivityThrottleSeconds { get; set; } = 60;

    /// <summary>Level3 普通用户闲置超时(分钟)。默认 30。</summary>
    public int IdleMinutesNormal { get; set; } = 30;

    /// <summary>Level3 MFA 用户闲置超时(分钟)。默认 15。</summary>
    public int IdleMinutesMfa { get; set; } = 15;
}

/// <summary>验证码配置(对应 <c>TenonAdmin:Security:Captcha</c>,设计 §3.2)。</summary>
public class AdminCaptchaOptions
{
    /// <summary>
    /// 是否启用登录验证码。<b>v1 默认关</b>:三行零配置 API 登录开箱即用;Web 模板/生产按需开
    /// (设计 §3.2 原写默认开,经权衡改默认关——账号级 LoginLock 已挡爆破主向,验证码作浏览器侧 opt-in 加固)。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 验证码类型(<c>ICaptchaProvider.Type</c>,零绘图依赖内置三选):
    /// <c>char</c>(字符 SVG,默认)| <c>path</c>(描边字形,明文不入标记、更抗爬)| <c>math</c>(算术)。
    /// 运行时以 DB 键 <c>sys.security.captcha.type</c> 覆盖本默认;图片/滑块/行为验证码走 <c>ICaptchaProvider</c> 前置替换。
    /// </summary>
    public string Type { get; set; } = "char";
}

/// <summary>登录失败锁定配置(对应 <c>TenonAdmin:Security:LoginLock</c>,设计 §3.2/§14)。</summary>
public class AdminLoginLockOptions
{
    /// <summary>连续密码错误多少次后锁定;&lt;=0 表示关闭该功能。</summary>
    public int MaxFailCount { get; set; } = 5;

    /// <summary>锁定时长(分钟);也是失败计数的滑动过期窗口——停手这么久后自动解锁。</summary>
    public int LockMinutes { get; set; } = 10;
}

/// <summary>
/// 请求限流配置(对应 <c>TenonAdmin:Security:RateLimit</c>,设计 §12/§14)。
/// <para>按<b>客户端 IP</b> 固定窗口限流:全局一档 + 认证端点(<c>/api/v1/auth/*</c>:登录/刷新/验证码)更严一档,
/// 防在线暴力破解与请求洪泛。经内置 <c>IStartupFilter</c> 挂载 <c>UseRateLimiter</c>,无需用户手动接中间件。</para>
/// <para>反向代理后取到的是代理 IP——上正式网关时需先接 <c>ForwardedHeaders</c> 中间件解析 X-Forwarded-For,
/// 否则同代理后所有客户端共用一个限流分区(ponytail:此处不预埋,见 <c>HttpContextCurrentUser.IpAddress</c> 同注)。</para>
/// </summary>
public class AdminRateLimitOptions
{
    /// <summary>配置键(GroupCode=<c>security</c>,与其他安全策略同 Tab)。运行时经 <c>RuntimeRateLimit</c> 快照读取。</summary>
    public const string KEY_ENABLED = "sys.security.rateLimit.enabled";
    public const string KEY_WINDOW = "sys.security.rateLimit.windowSeconds";
    public const string KEY_PERMIT = "sys.security.rateLimit.permitPerWindow";
    public const string KEY_AUTH_PERMIT = "sys.security.rateLimit.authPermitPerWindow";

    /// <summary>
    /// 是否启用限流(默认启用;§14 安全基线)。<b>此项为部署期硬总开关</b>:为 <c>false</c> 时无论 DB 配置如何都不限流
    /// (供消费方彻底关闭 / 测试隔离);为 <c>true</c>(默认)时,实际开关与阈值由 DB(<c>SysConfig</c>)运行时调控。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>限流窗口(秒),全局与认证端点共用同一窗口长度。</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>全局:单 IP 每窗口允许的请求数(宽松,只挡洪泛)。&lt;=0 视为不限全局。</summary>
    public int PermitPerWindow { get; set; } = 300;

    /// <summary>认证端点(<c>/api/v1/auth/*</c>):单 IP 每窗口允许的请求数(更严,挡在线爆破)。&lt;=0 视为不限。</summary>
    public int AuthPermitPerWindow { get; set; } = 20;
}

/// <summary>
/// 短信验证码配置(对应 <c>TenonAdmin:Security:SmsOtp</c>,设计 §14 登录加固)。
/// <para>两个开关是 DB 键(<c>sys.security.mfa.enabled</c> / <c>sys.security.smsLogin.enabled</c>)缺失时的兜底,
/// 运行时以 DB 配置为准(同验证码开关成法);数值参数为部署期配置(同验证码 TTL 成法)。</para>
/// </summary>
public class AdminSmsOtpOptions
{
    /// <summary>短信二次验证兜底开关(密码过后再验短信码;仅对绑定了手机号的用户生效,默认关)</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>短信验证码免密登录兜底开关(手机号+码直接登录,默认关)</summary>
    public bool LoginEnabled { get; set; }

    /// <summary>验证码位数(纯数字)</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>验证码有效期(秒);也是 MFA 挑战票据的有效期</summary>
    public int TtlSeconds { get; set; } = 300;

    /// <summary>同一手机号两次发送的最小间隔(秒)</summary>
    public int ResendSeconds { get; set; } = 60;

    /// <summary>单个验证码允许的错误尝试次数,达到即作废该码</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>单个手机号每日发送上限(防短信轰炸与费用失控)</summary>
    public int DailySendLimitPerPhone { get; set; } = 10;
}

/// <summary>
/// 安全配置(对应 <c>TenonAdmin:Security</c> 节,设计 §3.2/§14)。
/// v1 落 <see cref="Session"/> + <see cref="LoginLock"/> + <see cref="Captcha"/> + <see cref="RateLimit"/> + <see cref="SmsOtp"/>;
/// Level3 档位见 <see cref="Profile"/> + <see cref="DataProtection"/>。
/// </summary>
public class AdminSecurityOptions
{
    /// <summary>
    /// 安全档(对应 <c>TenonAdmin:Security:Profile</c>)。默认 <see cref="SecurityProfile.None"/>;
    /// 仅部署配置可信,不能经 SysConfig/管理页降级。
    /// </summary>
    public SecurityProfile Profile { get; set; } = SecurityProfile.None;

    /// <summary>会话并发策略(单端/多端/限并发数,见 <see cref="AdminSessionOptions"/>)</summary>
    public AdminSessionOptions Session { get; set; } = new();

    /// <summary>登录失败锁定策略(防暴力破解,见 <see cref="AdminLoginLockOptions"/>)</summary>
    public AdminLoginLockOptions LoginLock { get; set; } = new();

    /// <summary>验证码策略(默认关,见 <see cref="AdminCaptchaOptions"/>)</summary>
    public AdminCaptchaOptions Captcha { get; set; } = new();

    /// <summary>请求限流策略(按 IP,认证端点更严,见 <see cref="AdminRateLimitOptions"/>)</summary>
    public AdminRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>短信验证码策略(二次验证/免密登录,默认全关,见 <see cref="AdminSmsOtpOptions"/>)</summary>
    public AdminSmsOtpOptions SmsOtp { get; set; } = new();

    /// <summary>数据保护密钥(TOTP seed / HMAC 密钥等信封加密的主密钥材料,见 <see cref="AdminDataProtectionOptions"/>)</summary>
    public AdminDataProtectionOptions DataProtection { get; set; } = new();

    /// <summary>
    /// Level3 MFA/初始化授权等(对应 <c>TenonAdmin:Security:Level3</c>)。
    /// 仅部署配置可信;绑定/紧急恢复凭据消费后即失效。
    /// </summary>
    public AdminLevel3Options Level3 { get; set; } = new();

    /// <summary>
    /// 新建用户 / 重置密码时未显式给定密码的默认初始口令(对应 <c>TenonAdmin:Security:DefaultInitialPassword</c>)。
    /// <para><b>默认 null → 按账号生成密码学随机强口令</b>:安全默认,杜绝"随公开 NuGet 包分发的固定默认口令"这一
    /// 已知凭据弱点(CWE-798/1392)。重置密码会把随机口令返回给管理员当场转达;新建用户走随机口令时,
    /// 管理员需经"重置密码"取得可用口令(或建号时显式传密码)。</para>
    /// <para>需要可预期的统一初始口令(如内网批量建号)可显式配置本项;生产强烈建议保持 null 或配合首次登录强制改密(v1.x)。</para>
    /// </summary>
    public string? DefaultInitialPassword { get; set; }
}
