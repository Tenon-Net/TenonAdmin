using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// Level3 启动时幂等确保闲置账号扫描任务存在(新库种子 + 升级库补种)。
/// 非 Level3 不写库,避免改变既有非等保部署。
/// </summary>
public class Level3IdleAccountJobEnsureHostedService(
    IServiceScopeFactory scopes,
    ISecurityProfileAccessor profile,
    ILogger<Level3IdleAccountJobEnsureHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!profile.IsLevel3)
        {
            logger.LogDebug("非 Level3,跳过闲置账号扫描任务补种。");
            return;
        }

        using var scope = scopes.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SysJob>>();
        var existing = await jobs.GetFirstAsync(j => j.Code == DefaultJobSeed.IDLE_ACCOUNT_SCAN_CODE);
        if (existing is not null)
        {
            if (existing.Status == JobStatus.Ready && string.IsNullOrEmpty(existing.HandlerName))
            {
                existing.HandlerName = typeof(IdleAccountJob).FullName!;
                await jobs.UpdateAsync(existing);
            }
            return;
        }

        await jobs.InsertAsync(new SysJob
        {
            Code = DefaultJobSeed.IDLE_ACCOUNT_SCAN_CODE,
            Name = "闲置账号扫描",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(IdleAccountJob).FullName!,
            TriggerKind = JobTriggerKind.Cron,
            CronExpression = "0 15 4 * * ?",
            MisfireStrategy = JobMisfireStrategy.Skip,
            ConcurrencyMode = JobConcurrencyMode.SerialSkip,
            Status = JobStatus.Ready,
            IsSystem = true,
            AlertByNotice = true,
            Remark = "Level3 闲置账号治理(升级补种);60/90 天策略见 IIdleAccountService",
        });
        logger.LogInformation("已补种 Level3 闲置账号扫描任务 code={Code}", DefaultJobSeed.IDLE_ACCOUNT_SCAN_CODE);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
