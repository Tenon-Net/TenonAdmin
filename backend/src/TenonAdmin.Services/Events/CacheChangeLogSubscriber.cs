using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 事件总线的默认订阅者(演示 + 可观测性):启动时订阅字典/配置变更事件并落日志。
/// <para>它证明总线端到端可用(有生产者也有消费者),同时给出扩展点样板——真实场景可替换/追加订阅者
/// 做:多副本部署的跨节点缓存失效广播、审计、前端 SignalR 推送等。订阅在 <see cref="StartAsync"/> 建立、
/// <see cref="StopAsync"/> 释放(退订)。</para>
/// </summary>
public sealed class CacheChangeLogSubscriber(IEventBus events, ILogger<CacheChangeLogSubscriber> logger) : IHostedService
{
    // List<T> 非线程安全:StopAsync 遍历时若 StartAsync 仍在 Add(启动慢 + 收到 SIGTERM)、
    // 或 StopAsync 被重入,枚举器会抛 "Collection was modified"。两处变更都用 _subscriptions 作锁保护,
    // StopAsync 取快照后清空,令关闭幂等且不惧并发。
    private readonly List<IDisposable> _subscriptions = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dict = events.Subscribe<DictChangedEvent>((e, _) =>
        {
            logger.LogInformation("字典变更事件:类型码={TypeCode} 缓存已失效", e.TypeCode);
            return Task.CompletedTask;
        });
        var config = events.Subscribe<ConfigChangedEvent>((e, _) =>
        {
            logger.LogInformation("配置变更事件:键={Key} 缓存已失效", e.Key);
            return Task.CompletedTask;
        });
        lock (_subscriptions)
        {
            _subscriptions.Add(dict);
            _subscriptions.Add(config);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        IDisposable[] snapshot;
        lock (_subscriptions)
        {
            snapshot = [.. _subscriptions];
            _subscriptions.Clear();
        }
        foreach (var s in snapshot)
            s.Dispose();
        return Task.CompletedTask;
    }
}
