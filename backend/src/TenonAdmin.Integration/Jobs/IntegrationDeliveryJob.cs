using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 可靠投递扫描任务(编译类 <see cref="IAdminJob"/>):每次运行调用一轮 <see cref="IDeliveryDispatcher.RunOnceAsync"/>。
/// 多副本下由 <c>sys_job</c> 的数据库选主调度;手动执行或切主重叠时由投递记录的栅栏保证单赢家。
/// <c>TenonAdmin:Jobs:SchedulerEnabled=false</c> 的副本不执行投递。
/// </summary>
public class IntegrationDeliveryJob(IDeliveryDispatcher dispatcher) : IAdminJob
{
    /// <inheritdoc />
    public virtual async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var summary = await dispatcher.RunOnceAsync(cancellationToken);
        if (summary.Scanned > 0) context.Log?.Invoke("可靠投递:" + summary);
    }
}

/// <summary>投递扫描任务的种子(每 5 秒,串行跳过;运行态字段只在缺失时补入,不覆盖用户调整的节奏)。</summary>
internal sealed class IntegrationDeliveryJobSeed : ISeedData<SysJob>
{
    /// <summary>任务编码(排障/文档的稳定锚点)。</summary>
    internal const string Code = "itg-delivery";

    /// <summary>种子 Id:消费者号段 + 49_000。</summary>
    internal const long JobId = TenonSeedIds.ConsumerMin + 49_000;

    public bool SyncOnUpgrade => false;

    public IEnumerable<SysJob> HasData() =>
    [
        new SysJob
        {
            Id = JobId,
            Code = Code,
            Name = "第三方接入可靠投递",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(IntegrationDeliveryJob).FullName!,
            TriggerKind = JobTriggerKind.Interval,
            IntervalSeconds = 5,
            MisfireStrategy = JobMisfireStrategy.Skip,
            ConcurrencyMode = JobConcurrencyMode.SerialSkip,
            Status = JobStatus.Ready,
            NextRunTime = null,
            TimeoutSeconds = 0,
            IsSystem = false,
            AlertByNotice = true,
            Remark = "扫描到期的待处理、待确认投递与租约过期的处理中投递,调用适配器并回写",
        },
    ];
}
