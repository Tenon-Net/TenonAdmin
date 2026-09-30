using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 保留清理任务(编译类 <see cref="IAdminJob"/>):每天按 <see cref="IntegrationRetentionOptions"/> 清理过期记录。
/// 由 <see cref="IntegrationRetentionJobSeed"/> 播种任务行;任务管理页可暂停、改节奏或立即执行。
/// </summary>
public class IntegrationRetentionJob(IIntegrationRetentionService retention) : IAdminJob
{
    /// <inheritdoc />
    public virtual async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await retention.CleanupAsync(cancellationToken);
        context.Log?.Invoke("第三方接入保留清理:" + result);
    }
}

/// <summary>保留清理任务的种子(模块启用即存在;运行态字段只在缺失时补入,不覆盖用户调整的节奏)。</summary>
internal sealed class IntegrationRetentionJobSeed : ISeedData<SysJob>
{
    /// <summary>任务编码(排障/文档的稳定锚点)。</summary>
    internal const string Code = "itg-retention";

    /// <summary>种子 Id:消费者号段 + 49_001(49_000 留给投递扫描)。</summary>
    internal const long JobId = TenonSeedIds.ConsumerMin + 49_001;

    public bool SyncOnUpgrade => false;

    public IEnumerable<SysJob> HasData() =>
    [
        new SysJob
        {
            Id = JobId,
            Code = Code,
            Name = "第三方接入保留清理",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(IntegrationRetentionJob).FullName!,
            TriggerKind = JobTriggerKind.Cron,
            CronExpression = "0 30 3 * * ?",
            MisfireStrategy = JobMisfireStrategy.Skip,
            ConcurrencyMode = JobConcurrencyMode.SerialSkip,
            Status = JobStatus.Ready,
            TimeoutSeconds = 0,
            IsSystem = false,
            AlertByNotice = true,
            Remark = "按 TenonAdmin:Integration:Retention 清理过期的开放/出站调用记录与已结束的投递记录",
        },
    ];
}
