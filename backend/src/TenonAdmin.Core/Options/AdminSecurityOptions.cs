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
/// 安全配置(对应 <c>TenonAdmin:Security</c> 节,设计 §3.2/§14)。
/// v1 落 <see cref="Session"/> + <see cref="LoginLock"/>;验证码 / 密码策略随 T8 后续小轮补齐。
/// </summary>
public class AdminSecurityOptions
{
    /// <summary>会话并发策略(单端/多端/限并发数,见 <see cref="AdminSessionOptions"/>)</summary>
    public AdminSessionOptions Session { get; set; } = new();

    /// <summary>登录失败锁定策略(防暴力破解,见 <see cref="AdminLoginLockOptions"/>)</summary>
    public AdminLoginLockOptions LoginLock { get; set; } = new();

    /// <summary>验证码策略(默认关,见 <see cref="AdminCaptchaOptions"/>)</summary>
    public AdminCaptchaOptions Captcha { get; set; } = new();
}
