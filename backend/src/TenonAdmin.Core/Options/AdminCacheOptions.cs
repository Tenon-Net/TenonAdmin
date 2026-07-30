namespace TenonAdmin.Core;

/// <summary>
/// 缓存配置(对应 <c>TenonAdmin:Cache</c> 节,设计 §3.2)。
/// <para>
/// <b>Level3 约束</b>:必须使用 Redis 且启用认证与 TLS(<see cref="RequireTls"/> 或连接串含 <c>ssl=true</c>),
/// 不得退回进程内 Memory;缺配由启动预检/readiness 明确失败(见等保三级应用安全基线计划一期)。
/// </para>
/// </summary>
public class AdminCacheOptions
{
    /// <summary>提供者:<c>Memory</c>(默认,进程内)| <c>Redis</c>(装 TenonAdmin.Caching.Redis 可选包后可用)。Level3 强制 Redis。</summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>
    /// Redis 连接串(Provider=Redis 时必填)。
    /// Level3 须含密码(认证)与 TLS(<c>ssl=true</c> 或 <see cref="RequireTls"/>);预检会解析连接串核对这些项。
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// 是否要求 Redis TLS。Level3 下即使连接串未写 <c>ssl=true</c>,本项为 true 也表示部署声明强制 TLS;
    /// 预检同时接受连接串中的 <c>ssl=true</c>/<c>abortConnect</c> 等与本开关。
    /// </summary>
    public bool RequireTls { get; set; }

    /// <summary>缓存键前缀(共享缓存实例时的命名空间隔离,设计 §15 键均以此打头)</summary>
    public string KeyPrefix { get; set; } = "tenon:";

    /// <summary>
    /// 用户权限码缓存的过期分钟数。授权变更走<b>显式失效</b>(即时),这个 TTL 只是兜底——
    /// 防止有人绕过服务直接改库导致缓存长期陈旧。设 0 表示永不过期(仅靠显式失效)。
    /// </summary>
    public int PermissionMinutes { get; set; } = 20;
}
