using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 启动后执行一次 Level3 首启迁移(有旗标则跳过)。非 Level3 立即空跑退出。
/// <para>迁移失败 <b>fail-closed</b>:抛异常拒绝进入「已启用 Level3 但闲置锚点不可信」状态。</para>
/// </summary>
public sealed class Level3EnableMigrationHostedService(
    IServiceScopeFactory scopes,
    AdminSecurityOptions security,
    ILogger<Level3EnableMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (security.Profile != SecurityProfile.Level3) return;

        try
        {
            using var scope = scopes.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<ILevel3EnableMigrator>();
            var n = await migrator.EnsureMigratedAsync(cancellationToken);
            if (n > 0)
                logger.LogInformation("Level3 首启迁移完成:初始化 {Count} 名用户的 LastSuccessfulLoginAt", n);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Level3 首启迁移失败,拒绝启动(闲置账户锚点不可信)");
            throw new InvalidOperationException(
                "TenonAdmin Level3 首启迁移失败,拒绝启动。" +
                "LastSuccessfulLoginAt 未可靠初始化时不得以 Level3 运行。" +
                "请检查数据库连通性与权限后重启。详见内层异常。",
                ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
