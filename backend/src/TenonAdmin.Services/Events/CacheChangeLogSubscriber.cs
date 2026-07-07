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
    private readonly List<IDisposable> _subscriptions = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(events.Subscribe<DictChangedEvent>((e, _) =>
        {
            logger.LogInformation("字典变更事件:类型码={TypeCode} 缓存已失效", e.TypeCode);
            return Task.CompletedTask;
        }));
        _subscriptions.Add(events.Subscribe<ConfigChangedEvent>((e, _) =>
        {
            logger.LogInformation("配置变更事件:键={Key} 缓存已失效", e.Key);
            return Task.CompletedTask;
        }));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var s in _subscriptions)
            s.Dispose();
        _subscriptions.Clear();
        return Task.CompletedTask;
    }
}
