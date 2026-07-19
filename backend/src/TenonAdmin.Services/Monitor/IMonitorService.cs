namespace TenonAdmin.Services;

/// <summary>
/// 服务器监控服务(设计 §4 运维)——聚合进程与主机基础运行指标。注册为 <c>TryAddScoped</c>,
/// 方法 <c>virtual</c> 可覆写(如接自有指标源 / 屏蔽磁盘信息)。
/// </summary>
public interface IMonitorService
{
    /// <summary>取服务器运行快照(含一次约 500ms 的进程 CPU 采样)。</summary>
    Task<ServerInfoOutput> GetServerInfoAsync(CancellationToken cancellationToken = default);
}
