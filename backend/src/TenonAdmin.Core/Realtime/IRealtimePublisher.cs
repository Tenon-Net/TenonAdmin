namespace TenonAdmin.Core;

/// <summary>
/// 服务端→客户端实时推送通道(设计 §14 实时通知)。内核只定义抽象、不带任何长连接实现
/// (运行时依赖纪律:仅 SqlSugarCore + Microsoft.*);默认实现是<b>空操作</b>(<c>NoopRealtimePublisher</c>,
/// 关闭实时时业务代码照调不误)。AspNetCore 层在 <c>TenonAdmin:Realtime:Enabled</c> 开启时注册
/// 基于 SignalR 的真实现(SignalR 属 ASP.NET Core 共享框架,零新增 NuGet)。
/// <para>用途:公告发布即时推「notice-changed」让各端刷新未读角标(替代 30s 轮询);会话吊销即时推
/// 「force-logout」把被踢用户立刻登出(替代惰性 401)。二者均<b>纯推送</b>,不定义客户端可调方法。</para>
/// <para>接自有实时通道(如 WebSocket 网关 / MQ 扇出):实现本接口并在 <c>AddTenonAdmin()</c> 之前注册即接管
/// (TryAdd 前置替换,§5.2)。多副本要即时跨副本时,给内置 SignalR 叠 Redis backplane(消费者侧)。</para>
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
