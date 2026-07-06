namespace TenonAdmin.Core;

/// <summary>
/// 缓存配置(对应 <c>TenonAdmin:Cache</c> 节,设计 §3.2)。
/// </summary>
public class AdminCacheOptions
{
    /// <summary>提供者:<c>Memory</c>(默认,进程内)| <c>Redis</c>(装 TenonAdmin.Caching.Redis 可选包后可用)</summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>Redis 连接串(Provider=Redis 时必填)</summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>缓存键前缀(共享缓存实例时的命名空间隔离,设计 §15 键均以此打头)</summary>
    public string KeyPrefix { get; set; } = "tenon:";

    /// <summary>
    /// 用户权限码缓存的过期分钟数。授权变更走<b>显式失效</b>(即时),这个 TTL 只是兜底——
    /// 防止有人绕过服务直接改库导致缓存长期陈旧。设 0 表示永不过期(仅靠显式失效)。
    /// </summary>
    public int PermissionMinutes { get; set; } = 20;
}
