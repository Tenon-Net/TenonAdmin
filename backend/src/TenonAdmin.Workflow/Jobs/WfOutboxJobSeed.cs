using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// outbox 扫描任务的预置行。没有这行时,worker 虽然注册在 DI 中,调度器仍不会触发它。
/// </summary>
internal sealed class WfOutboxJobSeed : ISeedData<SysJob>
{
    /// <summary>任务编码(排障/文档的稳定锚点)。</summary>
    internal const string OUTBOX_SCAN_CODE = "wf-outbox-scan";

    private const long JobId = TenonSeedIds.ConsumerMin + 47_002;

    /// <summary>运行态任务只在缺失时补入,不在升级时覆盖用户调整的节奏和计数。</summary>
    public bool SyncOnUpgrade => false;

    public IEnumerable<SysJob> HasData() =>
    [
        new SysJob
        {
            Id = JobId,
            Code = OUTBOX_SCAN_CODE,
            Name = "工作流 outbox 扫描",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(WfOutboxJob).FullName!,

            TriggerKind = JobTriggerKind.Interval,
            IntervalSeconds = 5,
            MisfireStrategy = JobMisfireStrategy.Skip,
            ConcurrencyMode = JobConcurrencyMode.SerialSkip,
            Status = JobStatus.Ready,
            NextRunTime = null,
            TimeoutSeconds = 0,
            IsSystem = false,
            AlertByNotice = true,
            Remark = "扫描 Pending 和可见性超时 Dispatching 的工作流 outbox 并投递",
        },
    ];
}
