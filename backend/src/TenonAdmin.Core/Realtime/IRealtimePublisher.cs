namespace TenonAdmin.Core;

/// <summary>
/// 可替换的服务端推送通道。默认实现为空操作;启用实时通知时由 AspNetCore 层注册 SignalR 实现。
/// <para>推送用于提前触发未读刷新和会话下线;客户端仍保留轮询,服务端仍在请求时校验会话,
/// 以覆盖实时关闭、断线或跨副本未送达。此通道不定义客户端可调方法。</para>
/// <para>消费者在 <c>AddTenonAdmin()</c> 之前注册本接口即可接管。内置实现只向本副本连接推送,
/// 跨副本即时推送需由消费者配置 SignalR backplane。</para>
/// </summary>
public interface IRealtimePublisher
{
    /// <summary>推送一条事件到某用户的<b>全部</b>在线连接(不限会话)。</summary>
    /// <param name="userId">目标用户 Id</param>
    /// <param name="event">事件名(客户端据此路由处理,如 <c>notice-changed</c>)</param>
    /// <param name="data">可选负载(小对象即可;大数据让客户端回查,推送只作信号)</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken cancellationToken = default);

    /// <summary>广播一条事件到<b>所有</b>在线连接(如全体公告)。</summary>
    Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default);

    /// <summary>推送一条事件到某<b>会话</b>(sid)的连接——精确到单次登录,用于 force-logout 只踢被吊销的那次会话,
    /// 不误伤同一用户的其他登录。</summary>
    Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default);
}
