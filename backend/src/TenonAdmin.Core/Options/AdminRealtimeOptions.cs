namespace TenonAdmin.Core;

/// <summary>
/// 实时通知配置(对应 <c>TenonAdmin:Realtime</c> 节,设计 §14 实时通知)。
/// <para><see cref="Enabled"/> 默认 <c>false</c>:不开启 = 完全维持既有行为(公告 30s 轮询、强退惰性 401),
/// 实时是<b>纯增强</b>。开启后 AspNetCore 层挂 SignalR Hub 并注册基于它的 <see cref="IRealtimePublisher"/>
/// (SignalR 属 ASP.NET Core 共享框架,零新增 NuGet)。</para>
/// </summary>
public class AdminRealtimeOptions
{
    /// <summary>是否开启实时推送(默认关);关闭时推送为空操作,不挂 Hub、不建长连接。</summary>
    public bool Enabled { get; set; }

    /// <summary>SignalR Hub 的映射路径(默认 <c>/hub/realtime</c>);JWT 从该路径的 query <c>access_token</c> 读取。</summary>
    public string HubPath { get; set; } = "/hub/realtime";
}
