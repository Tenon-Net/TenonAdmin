# Event Bus

After `DictService.InvalidateAsync` invalidates a dictionary's cache, it also broadcasts a `DictChangedEvent`. Who cares, and what they do about it, is none of `DictService`'s business — and it doesn't need to be.

## Contract

```csharp
public interface IEventBus
{
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : notnull;
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : notnull;
}
```

`PublishAsync` returns as soon as the event is written to the channel — it doesn't wait for subscribers to finish handling it. `Subscribe` returns an `IDisposable`; disposing it unsubscribes. There's no separate `Unsubscribe` method.

## Default implementation: `ChannelEventBus`

The kernel's default registration, `ChannelEventBus`, is built on an unbounded `System.Threading.Channels` channel: publishing just writes the event to the channel, and a single background loop reads it back out and dispatches it to subscribers in order. That design draws a few hard boundaries:

- **In-process, not persisted.** Events live in memory only — on a process restart, whatever wasn't consumed is simply gone. There's no cross-replica queue.
- **Subscribers for the same event run sequentially, in registration order — not fanned out concurrently.** A slow subscriber delays the ones registered after it, but never the publisher: the publisher already returned the moment the write hit the channel.
- **A single subscriber's exception is isolated to itself.** The dispatch loop swallows it internally — no rethrow, no retry, and it never takes down other subscribers or later events. `Core` has no `ILogger`, so logging the exception is each subscriber's own responsibility inside its `try/catch`.
- **No cross-replica delivery.** Under multiple instances, an event published on replica A never reaches a subscriber on replica B — the same limitation as the in-process cache.

## Using it

The kernel ships an end-to-end sample out of the box: `CacheChangeLogSubscriber` (an `IHostedService` that subscribes on start and unsubscribes on stop) listens for dictionary and config change events and logs them. Its purpose is to prove the bus actually works end to end, and along the way it shows what an extension point here looks like:

```csharp
// Subscribe: CacheChangeLogSubscriber.StartAsync
var dict = events.Subscribe<DictChangedEvent>((e, _) =>
{
    logger.LogInformation("Dict changed: type={TypeCode}, cache invalidated", e.TypeCode);
    return Task.CompletedTask;
});

// Publish: DictService.InvalidateAsync — invalidate the cache first, then broadcast
await cache.RemoveAsync(CacheKeys.DictItems(typeCode));
await events.PublishAsync(new DictChangedEvent(typeCode));

// Unsubscribe: dispose the IDisposable Subscribe returned
dict.Dispose();
```

`IEventBus` is registered `Singleton` — the same lifetime as the other stateless services (the hasher, the captcha generator, cache providers).

## Replacing it: cross-process fan-out

The default implementation is single-process. To make events genuinely fan out across replicas — say, propagating cache invalidation to every instance in a multi-replica deployment — swap in an `IEventBus` implementation backed by RabbitMQ or Kafka. The replacement path is the same as any other extension point: register it before `AddTenonAdmin()`:

```csharp
builder.Services.AddSingleton<IEventBus, RabbitMqEventBus>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

Internally the kernel uses `TryAddSingleton`, so whichever implementation is registered first wins — the publishers (`DictService`, `ConfigService`, and the rest) don't change a line.
