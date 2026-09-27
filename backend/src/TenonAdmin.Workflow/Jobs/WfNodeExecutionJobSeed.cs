using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 节点 execution 扫描任务的预置行。没有这行时，worker 虽然注册在 DI 中，既有
/// <see cref="JobSchedulerService"/> 仍不会触发它。
/// </summary>
internal sealed class WfNodeExecutionJobSeed : ISeedData<SysJob>
{
    /// <summary>任务编码(排障/文档的稳定锚点)。</summary>
    internal const string NODE_EXECUTION_SCAN_CODE = "wf-node-execution-scan";

    private const long JobId = TenonSeedIds.ConsumerMin + 47_001;

    /// <summary>运行态任务只在缺失时补入，不在升级时覆盖用户调整的节奏和计数。</summary>
    public bool SyncOnUpgrade => false;

    public IEnumerable<SysJob> HasData() =>
    [
        new SysJob
        {
            Id = JobId,
            Code = NODE_EXECUTION_SCAN_CODE,
            Name = "工作流节点执行扫描",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(WfNodeExecutionJob).FullName!,

            TriggerKind = JobTriggerKind.Interval,
            IntervalSeconds = 5,
            MisfireStrategy = JobMisfireStrategy.Skip,
            ConcurrencyMode = JobConcurrencyMode.SerialSkip,
            Status = JobStatus.Ready,
            NextRunTime = null,
            // 节点 execution 自己有 handler deadline/lease；任务层不抢先取消整批。
            TimeoutSeconds = 0,
            IsSystem = false,
            AlertByNotice = true,
            Remark = "扫描 Pending、到期 RetryScheduled 和租约过期 Running 的工作流节点 execution",
        },
    ];
}
