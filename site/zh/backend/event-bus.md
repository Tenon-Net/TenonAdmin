# 事件总线

`DictService.InvalidateAsync` 让字典缓存失效之后，还广播一个 `DictChangedEvent`。谁在乎这件事、要不要跟着做点什么，`DictService` 自己并不知道，也不需要知道。

## 契约

```csharp
public interface IEventBus
{
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : notnull;
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : notnull;
}
```

`PublishAsync` 写进通道就返回，不等订阅者把事件处理完。`Subscribe` 拿到的 `IDisposable` 释放即退订，没有单独的 `Unsubscribe` 方法。

## 默认实现：`ChannelEventBus`

内核默认注册的 `ChannelEventBus` 基于 `System.Threading.Channels` 的无界通道：发布只是把事件写进通道，一个后台循环单独把它们顺序读出来分发给订阅者。这条边界决定了它的几条行为：

- **进程内、不落盘**。事件只活在内存里，进程一重启，没消费的事件直接没了，也没有跨副本的队列。
- **同一事件的多个订阅者按注册顺序依次执行，不是并发扇出**。某个订阅者处理得慢，会拖慢排在它后面的订阅者，但拖不慢发布方，因为写通道那一刻就已经返回。
- **单个订阅者抛异常只隔离它自己**。派发循环把异常吞在内部，不外抛、不重试，也不连累同一事件的其他订阅者或后续事件。`Core` 层没有 `ILogger`，记录异常的责任落在订阅者自己的 `try/catch` 里。
- **不跨副本**。多实例部署下，A 副本发布的事件，B 副本上的订阅者收不到，这一点和进程内缓存是同一类限制。

## 怎么用

内核自带一个端到端的样板：`CacheChangeLogSubscriber`（`IHostedService`，启动时订阅、停止时退订）订阅字典和配置的变更事件，收到就打一条日志，用意是证明总线真的能跑通，顺带演示替换点该长什么样：

```csharp
// 订阅：CacheChangeLogSubscriber.StartAsync
var dict = events.Subscribe<DictChangedEvent>((e, _) =>
{
    logger.LogInformation("字典变更事件:类型码={TypeCode} 缓存已失效", e.TypeCode);
    return Task.CompletedTask;
});

// 发布：DictService.InvalidateAsync，字典项改动后先失效缓存、再广播
await cache.RemoveAsync(CacheKeys.DictItems(typeCode));
await events.PublishAsync(new DictChangedEvent(typeCode));

// 退订：释放 Subscribe 返回的 IDisposable
dict.Dispose();
```

`IEventBus` 以 `Singleton` 注册，和哈希器、验证码生成器、缓存 provider 这些无状态服务同一个生命周期。

## 替换：跨进程分发

默认实现是单进程内的，要让事件真正跨副本广播，比如多实例部署下把缓存失效联动到所有副本，就得换一个接 RabbitMQ/Kafka 的 `IEventBus` 实现。替换路径和其他扩展点一样，在 `AddTenonAdmin()` 之前抢注册：

```csharp
builder.Services.AddSingleton<IEventBus, RabbitMqEventBus>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

内核内部用的是 `TryAddSingleton`，所以这里先注册的实现会赢，业务代码（`DictService`、`ConfigService` 这些发布方）不用改一行。
