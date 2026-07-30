using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// Level3 启动闸门:Profile=Level3 时运行预检,关键项失败则抛 <see cref="InvalidOperationException"/> 拒绝启动。
/// 非 Level3 不阻断(生产仅由 <see cref="SecurityProfileWarningHostedService"/> 告警)。
/// </summary>
internal sealed class Level3StartupValidationHostedService(
    IServiceScopeFactory scopeFactory,
    ISecurityProfileAccessor profile,
    ILogger<Level3StartupValidationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!profile.IsLevel3)
        {
            logger.LogDebug("Security Profile 非 Level3,跳过 Level3 启动预检闸门。");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var precheck = scope.ServiceProvider.GetRequiredService<ILevel3PrecheckService>();
        var result = await precheck.RunAsync(cancellationToken);

        if (!result.HasCriticalFailures)
        {
            logger.LogInformation(
                "Level3 启动预检通过(capability={Version}, overallPhase1={Ok})。未实现二/三期强制项 {Count} 项(见安全基线 API)。",
                result.CapabilityVersion,
                result.OverallCompliantForPhase1,
                result.UnimplementedMandates.Count);
            return;
        }

        var ids = string.Join(", ", result.CriticalFailureIds);
        var details = string.Join("; ",
            result.Checks
                .Where(c => c.Critical && c.Status == Level3CheckStatus.Fail)
                .Select(c => $"{c.Id}: {c.Message} => {c.Remediation}"));

        throw new InvalidOperationException(
            "TenonAdmin Level3 安全档启动预检失败,拒绝启动。" +
            $"criticalFailures=[{ids}]。{details} " +
            "详情见 ILevel3PrecheckService / GET /api/v1/sys/security/baseline。" +
            "内核不宣称已通过等保三级;修复配置后重启。");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
