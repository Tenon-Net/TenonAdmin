namespace TenonAdmin.Core;

/// <summary>会话并发模式(设计 §15/§3.2 <c>Security:Session:Mode</c>)。</summary>
public enum SessionMode
{
    /// <summary>多端并存(默认):同一用户可有多个活跃会话</summary>
    Multi,

    /// <summary>单端:新登录吊销该用户其他所有会话(挤下线)</summary>
    Single,
}

/// <summary>会话配置(对应 <c>TenonAdmin:Security:Session</c>,设计 §15)。</summary>
public class AdminSessionOptions
{
    /// <summary>并发模式:Multi(默认)| Single(新登录踢旧)</summary>
    public SessionMode Mode { get; set; } = SessionMode.Multi;

    /// <summary>最大并发会话数;&gt;0 时超出则按最早登录吊销最旧会话。0 = 不限。</summary>
    public int MaxConcurrent { get; set; }
}

/// <summary>验证码配置(对应 <c>TenonAdmin:Security:Captcha</c>,设计 §3.2)。</summary>
public class AdminCaptchaOptions
{
    /// <summary>
    /// 是否启用登录验证码。<b>v1 默认关</b>:三行零配置 API 登录开箱即用;Web 模板/生产按需开
    /// (设计 §3.2 原写默认开,经权衡改默认关——账号级 LoginLock 已挡爆破主向,验证码作浏览器侧 opt-in 加固)。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>验证码类型:<c>Svg</c>(默认,零绘图依赖);图片/滑块走 <c>ICaptchaProvider</c> 扩展点。</summary>
    public string Type { get; set; } = "Svg";
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
    /// <summary>是否启用限流(默认启用;§14 安全基线)。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>限流窗口(秒),全局与认证端点共用同一窗口长度。</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>全局:单 IP 每窗口允许的请求数(宽松,只挡洪泛)。&lt;=0 视为不限全局。</summary>
    public int PermitPerWindow { get; set; } = 300;

    /// <summary>认证端点(<c>/api/v1/auth/*</c>):单 IP 每窗口允许的请求数(更严,挡在线爆破)。&lt;=0 视为不限。</summary>
    public int AuthPermitPerWindow { get; set; } = 20;
}

/// <summary>
/// 安全配置(对应 <c>TenonAdmin:Security</c> 节,设计 §3.2/§14)。
/// v1 落 <see cref="Session"/> + <see cref="LoginLock"/> + <see cref="Captcha"/> + <see cref="RateLimit"/>。
/// </summary>
public class AdminSecurityOptions
{
    /// <summary>会话并发策略(单端/多端/限并发数,见 <see cref="AdminSessionOptions"/>)</summary>
    public AdminSessionOptions Session { get; set; } = new();

    /// <summary>登录失败锁定策略(防暴力破解,见 <see cref="AdminLoginLockOptions"/>)</summary>
    public AdminLoginLockOptions LoginLock { get; set; } = new();

    /// <summary>验证码策略(默认关,见 <see cref="AdminCaptchaOptions"/>)</summary>
    public AdminCaptchaOptions Captcha { get; set; } = new();

    /// <summary>请求限流策略(按 IP,认证端点更严,见 <see cref="AdminRateLimitOptions"/>)</summary>
    public AdminRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// 新建用户 / 重置密码时未显式给定密码的默认初始口令(对应 <c>TenonAdmin:Security:DefaultInitialPassword</c>)。
    /// <para><b>默认 null → 按账号生成密码学随机强口令</b>:安全默认,杜绝"随公开 NuGet 包分发的固定默认口令"这一
    /// 已知凭据弱点(CWE-798/1392)。重置密码会把随机口令返回给管理员当场转达;新建用户走随机口令时,
    /// 管理员需经"重置密码"取得可用口令(或建号时显式传密码)。</para>
    /// <para>需要可预期的统一初始口令(如内网批量建号)可显式配置本项;生产强烈建议保持 null 或配合首次登录强制改密(v1.x)。</para>
    /// </summary>
    public string? DefaultInitialPassword { get; set; }
}
