using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 实时通知(SignalR)行为锁。默认关:业务照调 <see cref="IRealtimePublisher"/> 走空实现、Hub 不映射(维持轮询/惰性 401);
/// 开启:换成 SignalR 真实现 + 映射带鉴权的 Hub。两处触发(会话吊销→force-logout、公告发布→notice-changed)用
/// 记录型假 publisher 锁死接线,不依赖真 SignalR 客户端(浏览器端真链路走手动冒烟)。
/// </summary>
public class RealtimeTests
{
    /// <summary>记录型假 publisher:把每次推送记成 (方法, 目标, 事件),供断言接线是否触发。</summary>
    private sealed class RecordingRealtimePublisher : IRealtimePublisher
    {
        public readonly List<(string Method, string Target, string Event)> Calls = [];

        public Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(("user", userId.ToString(), @event));
            return Task.CompletedTask;
        }

        public Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(("all", "", @event));
            return Task.CompletedTask;
        }

        public Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(("session", sessionId, @event));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Default_realtime_publisher_is_noop()
    {
        using var f = new AdminAppFactory();   // Realtime 默认关
        Assert.IsType<NoopRealtimePublisher>(f.Services.GetRequiredService<IRealtimePublisher>());
    }

    [Fact]
    public async Task Revoking_a_session_pushes_force_logout()
    {
        var recorder = new RecordingRealtimePublisher();
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IRealtimePublisher>(recorder)),
        };
        using var scope = f.Services.CreateScope();

        // 所有下线路径都汇聚到 RevokeAsync;此处直接调它,证明吊销即推 force-logout 到该会话(sid)
        await scope.ServiceProvider.GetRequiredService<ISessionService>().RevokeAsync("sid-to-kick");

        Assert.Contains(recorder.Calls, c => c == ("session", "sid-to-kick", "force-logout"));
    }

    [Fact]
    public async Task Publishing_a_notice_pushes_notice_changed()
    {
        var recorder = new RecordingRealtimePublisher();
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IRealtimePublisher>(recorder)),
        };
        using var scope = f.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<INoticeService>()
            .PublishAsync(new NoticePublishInput { Title = "hello", ReceiverType = ReceiverType.All });

        // 发布即广播 notice-changed,让各端自查未读角标(替代 30s 轮询)
        Assert.Contains(recorder.Calls, c => c == ("all", "", "notice-changed"));
    }

    [Fact]
    public async Task Enabled_realtime_registers_signalr_publisher_and_maps_hub()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Realtime:Enabled"] = "true" },
        };

        // 开启后真实现压过 Noop(TryAdd 先到者胜,注册在 AddTenonAdminServices 之前)
        Assert.IsType<SignalRRealtimePublisher>(f.Services.GetRequiredService<IRealtimePublisher>());

        // Hub 已映射且带 [Authorize]:无令牌访问 → 401(若未映射会是 404),证明 MapHub + 鉴权都接上了
        var resp = await f.CreateClient().GetAsync("/hub/realtime");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Disabled_realtime_does_not_map_hub()
    {
        using var f = new AdminAppFactory();   // Realtime 默认关
        var resp = await f.CreateClient().GetAsync("/hub/realtime");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);   // 未映射 → 404
    }
}
