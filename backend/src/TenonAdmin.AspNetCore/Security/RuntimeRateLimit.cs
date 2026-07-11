using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 限流参数的运行时快照。限流器在<b>启动期一次性构建</b>、分区选择器是<b>同步</b>的,拿不到 per-request 的异步
/// DB 读;故用一个单例快照:启动时从 <c>SysConfig</c>(缺失/解析失败回退 Options)载入,并订阅
/// <see cref="ConfigChangedEvent"/> 在改配置时刷新。选择器同步读 <see cref="Current"/>,配合「把阈值编进分区键」
/// 使改动即时生效(旧分区空闲后由 <c>PartitionedRateLimiter</c> 自动回收)。
/// <para>与其他配置一样是<b>可替换</b>的:<c>TryAddSingleton</c> 注册,消费方可前置注册同类型覆盖。</para>
/// </summary>
public sealed class RuntimeRateLimit(
    IServiceProvider services,
    IEventBus events,
    AdminSecurityOptions security,
    ILogger<RuntimeRateLimit> logger) : IHostedService
{
    /// <summary>一次原子读到的一整组限流参数(引用整体替换,选择器不会读到撕裂的中间态)。</summary>
    public sealed record Snapshot(bool Enabled, int WindowSeconds, int PermitPerWindow, int AuthPermitPerWindow);

    private volatile Snapshot? _current;
    private IDisposable? _sub;

    /// <summary>当前生效快照;尚未载入时回退 Options 默认。</summary>
    public Snapshot Current => _current ?? Fallback();

    private Snapshot Fallback()
    {
        var rl = security.RateLimit;
        return new(rl.Enabled, rl.WindowSeconds, rl.PermitPerWindow, rl.AuthPermitPerWindow);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync();
        // 键少 → 任一限流键变更即整体重载最省心。ChannelEventBus 是进程内异步:改配置到快照生效有极短最终一致窗口。
        _sub = events.Subscribe<ConfigChangedEvent>(async (e, _) =>
        {
            if (e.Key.StartsWith("sys.security.rateLimit.", StringComparison.Ordinal))
                await RefreshAsync();
        });
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sub?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 DB 重载快照(每键 DB 优先、Options 兜底)。公开以便测试确定性刷新 / 运维手动强刷。
    /// <para>Enabled 采「硬总开关与 DB 相与」:Options 关则恒关(<see cref="AdminRateLimitOptions.Enabled"/> 注释)。</para>
    /// </summary>
    // ponytail: 启动早于建表/种子时读会抛 → try/catch 回退 Options 默认(= 种子默认),后续任一改配置事件会再拉一次。
    public async Task RefreshAsync()
    {
        try
        {
            using var scope = services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfigService>();
            var rl = security.RateLimit;

            var dbEnabled = bool.TryParse(await config.GetValueByKeyAsync(AdminRateLimitOptions.KEY_ENABLED), out var en) ? en : rl.Enabled;
            var window = int.TryParse(await config.GetValueByKeyAsync(AdminRateLimitOptions.KEY_WINDOW), out var w) && w > 0 ? w : rl.WindowSeconds;
            var permit = int.TryParse(await config.GetValueByKeyAsync(AdminRateLimitOptions.KEY_PERMIT), out var p) ? p : rl.PermitPerWindow;
            var authPermit = int.TryParse(await config.GetValueByKeyAsync(AdminRateLimitOptions.KEY_AUTH_PERMIT), out var ap) ? ap : rl.AuthPermitPerWindow;

            _current = new Snapshot(rl.Enabled && dbEnabled, window, permit, authPermit);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "限流配置加载失败,暂用 Options 默认");
        }
    }
}
