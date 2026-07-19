using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IRealtimePublisher"/> 默认实现:<b>空操作</b>(不建任何长连接)。
/// <para>这是关闭实时时的默认通道——业务代码(会话吊销、公告发布)照调 <see cref="IRealtimePublisher"/> 不误,
/// 只是没有连接可推。开启 <c>TenonAdmin:Realtime:Enabled</c> 后 AspNetCore 层前置注册基于 SignalR 的真实现即接管
/// (TryAdd 前置替换,§5.2);消费方也可注册自有实时通道整体替换。</para>
/// </summary>
public class NoopRealtimePublisher : IRealtimePublisher
{
    /// <inheritdoc />
    public Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
