namespace TenonAdmin.Core;

/// <summary>
/// Level3 缓存安全能力契约(由真实 Redis 等分布式缓存实现声明)。
/// 预检只依赖本接口与探针结果,不依赖实现类名是否包含 "Redis"。
/// </summary>
public interface ISecureCacheCapabilities
{
    /// <summary>是否为跨进程/跨副本的分布式缓存后端(Level3 要求 true)。</summary>
    bool IsDistributed { get; }

    /// <summary>连接是否配置了认证(密码等)。</summary>
    bool HasAuthenticationConfigured { get; }

    /// <summary>连接是否强制/声明了 TLS。</summary>
    bool HasTlsConfigured { get; }

    /// <summary>
    /// 连接可用性探针(如 PING)。返回值第二项为脱敏说明,失败时 Level3 应 fail-closed。
    /// </summary>
    Task<(bool Ok, string Message)> ProbeAsync(CancellationToken cancellationToken = default);
}
