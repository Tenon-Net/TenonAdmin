using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TenonAdmin.Core;

/// <summary>
/// <see cref="IEventBus"/> 的默认进程内实现(设计 §2.2/§139):以 <c>System.Threading.Channels</c>
/// 无界通道承接发布,单个后台循环顺序派发给订阅者。
/// <para>发布只是把事件写入通道(非阻塞、瞬时返回),真正的处理在后台线程——故慢订阅者不拖累请求线程。</para>
/// <para>单实例注册;进程内语义。跨进程分发(多副本部署共享失效)请替换本实现,接 RabbitMQ/Kafka。
/// 容器关闭时经 <see cref="DisposeAsync"/> 停派发循环、排空通道。</para>
/// </summary>
public sealed class ChannelEventBus : IEventBus, IAsyncDisposable
{
    // 派发信封:携带事件对象与其运行时类型;后台循环据类型精确找订阅者(避免反射/装箱歧义)。
    private readonly record struct Envelope(Type EventType, object Event);

    // 无界单读通道:发布多写、后台单读。SingleReader=true 让运行时走更省的单消费者路径。
    private readonly Channel<Envelope> _channel = Channel.CreateUnbounded<Envelope>(
        new UnboundedChannelOptions { SingleReader = true });

    // 每种事件类型 → 一组处理器。内层用 ConcurrentDictionary(键为订阅句柄 Guid)兼顾并发订阅/退订与
    // 派发时的无锁枚举——ConcurrentDictionary 的 Values 枚举本身线程安全,订阅是稀有操作、派发是热路径。
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Func<object, CancellationToken, Task>>> _handlers = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _dispatchLoop;

    public ChannelEventBus() => _dispatchLoop = Task.Run(DispatchLoopAsync);

    /// <inheritdoc/>
    public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : notnull
        => _channel.Writer.WriteAsync(new Envelope(typeof(TEvent), @event), cancellationToken);

    /// <inheritdoc/>
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : notnull
    {
        var type = typeof(TEvent);
        var id = Guid.NewGuid();
        // 把强类型处理器适配为 object 处理器存起来;派发时事件类型确定,强转安全。
        Func<object, CancellationToken, Task> boxed = (e, ct) => handler((TEvent)e, ct);
        _handlers.GetOrAdd(type, _ => new()).TryAdd(id, boxed);
        return new Subscription(this, type, id);
    }

    // 后台派发循环:顺序读通道、按类型分发。单个订阅者抛异常只吞在其内(隔离故障),不阻断
    // 其他订阅者与后续事件——Core 无 ILogger,记录责任交由订阅者自身(其内可 try/catch + 日志)。
    private async Task DispatchLoopAsync()
    {
        try
        {
            await foreach (var env in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                if (!_handlers.TryGetValue(env.EventType, out var handlers))
                    continue;
                foreach (var handler in handlers.Values)
                {
                    try
                    {
                        await handler(env.Event, _cts.Token);
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                    {
                        return; // 关闭中,停派发
                    }
                    catch
                    {
                        // ponytail: 故意隔离单订阅者故障,不外抛;订阅者应自行 try/catch 记日志。
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭(通道被 complete / token 取消)
        }
    }

    private void Unsubscribe(Type type, Guid id)
    {
        if (_handlers.TryGetValue(type, out var handlers))
            handlers.TryRemove(id, out _);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete(); // 不再接收发布
        _cts.Cancel();                 // 唤醒并终止派发循环
        try { await _dispatchLoop; } catch { /* 关闭期异常无碍 */ }
        _cts.Dispose();
    }

    // 退订句柄:释放即从处理器表移除自己(幂等——重复 Dispose 无副作用)。
    private sealed class Subscription(ChannelEventBus bus, Type type, Guid id) : IDisposable
    {
        public void Dispose() => bus.Unsubscribe(type, id);
    }
}
