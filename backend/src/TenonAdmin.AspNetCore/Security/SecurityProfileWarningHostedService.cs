using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 历史：生产未开 Level3 时打印「不满足三级基线」告警。
/// ADR 0006：完整 Level3 不再是产品目标；本服务保留注册位以免破坏 DI 图，启动时为空操作。
/// 后续瘦身可整类删除。
/// </summary>
internal sealed class SecurityProfileWarningHostedService(
    ILogger<SecurityProfileWarningHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "SecurityProfileWarningHostedService: ADR 0006 后不再对未启用 Profile=Level3 告警。");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
