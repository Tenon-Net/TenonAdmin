namespace TenonAdmin.Core;

/// <summary>
/// 进程内事件总线(扩展点,设计 §2.2/§5.5)——发布/订阅解耦。默认实现为
/// <c>System.Threading.Channels</c> 支撑的进程内总线(<c>ChannelEventBus</c>);
/// 典型替换:接 RabbitMQ/Kafka 做跨进程分发。
/// <para>发布非阻塞(写入通道即返回),订阅者在后台按序消费——慢订阅者不拖累请求线程。</para>
/// </summary>
public interface IEventBus
{
    /// <summary>发布一个事件(写入通道,后台派发给订阅者)。</summary>
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : notnull;

    /// <summary>订阅某类型事件;返回的 <see cref="IDisposable"/> 释放即退订。</summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : notnull;
}
