using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 历史：Profile=Level3 时预检关键项失败则拒绝启动。
/// ADR 0006：可选安全不得以 fail-closed 总档阻断启动；本服务不再抛异常。
/// 若仍配置了 Profile=Level3，仅打日志提示应迁移到独立开关（见 security-optional-slim-plan）。
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
            logger.LogDebug("Security Profile 非 Level3，跳过历史 Level3 启动预检。");
            return;
        }

        logger.LogWarning(
            "TenonAdmin:Security:Profile=Level3 已废弃为产品路径(ADR 0006)。" +
            "启动不再因预检失败而拒绝；请迁移到独立可选开关(Totp/Cookie 等)，勿再依赖 Level3 总档。");

        // 仍可跑一遍预检便于本地观察，但结果只记日志，不 fail-closed。
        try
        {
            using var scope = scopeFactory.CreateScope();
            var precheck = scope.ServiceProvider.GetService<ILevel3PrecheckService>();
            if (precheck is null)
                return;

            var result = await precheck.RunAsync(cancellationToken).ConfigureAwait(false);
            var criticalFails = result.Checks
                .Where(c => c.Critical && c.Status == Level3CheckStatus.Fail)
                .Select(c => c.Id)
                .ToList();

            if (criticalFails.Count == 0)
            {
                logger.LogInformation(
                    "历史 Level3 预检通过(capability={Version})；产品路径仍以 ADR 0006 可选开关为准。",
                    result.CapabilityVersion);
            }
            else
            {
                logger.LogWarning(
                    "历史 Level3 预检存在失败项(不阻断启动): {Ids}",
                    string.Join(", ", criticalFails));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "历史 Level3 预检执行异常(不阻断启动)。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
