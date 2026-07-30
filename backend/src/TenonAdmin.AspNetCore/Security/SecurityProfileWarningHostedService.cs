using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 生产环境未启用 <see cref="SecurityProfile.Level3"/> 时打印明确告警。
/// 不阻断启动(ADR 0005:Level3 显式启用、默认不启用,避免破坏性升级);
/// 预检报告另标记为未满足三级应用安全基线。内核不宣称「已通过等保三级」。
/// </summary>
internal sealed class SecurityProfileWarningHostedService(
    IHostEnvironment env,
    AdminSecurityOptions security,
    ILogger<SecurityProfileWarningHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (env.IsProduction() && security.Profile != SecurityProfile.Level3)
        {
            logger.LogWarning(
                "TenonAdmin: 生产环境未启用 TenonAdmin:Security:Profile=Level3。" +
                "当前部署不满足等保三级应用安全基线;若需该基线请显式配置 Profile=Level3 并完成预检。" +
                "内核不宣称已通过等保三级。");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
