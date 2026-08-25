using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 超时扫描任务的预置行。<b>没有这一行,超时策略在真实部署里永不触发</b>——调度器只派发
/// <c>sys_job</c> 表里 <c>Status = Ready</c> 的行,<c>TryAddEnumerable</c> 注册只是让
/// <see cref="WfTimeoutJob"/> 可被处理器解析器选到。
/// </summary>
internal sealed class WfTimeoutJobSeed : ISeedData<SysJob>
{
    /// <summary>任务编码(排障/文档的稳定锚点)。</summary>
    internal const string TIMEOUT_SCAN_CODE = "wf-timeout-scan";

    /// <summary>
    /// 与 <c>WorkflowMenuSeed</c> 同一取号规则(消费者段 + 包保留偏移 47_000):卫星包不属于内核程序集,
    /// 故受**消费者**下界(<c>TenonSeedIds.ConsumerMin</c>)约束。<c>sys_job</c> 的 Id 空间与 <c>sys_menu</c>
    /// 独立,与菜单根同值不撞车。
    /// <para><b>守住这条的不是 <c>SeedIdRangeTests</c>。</b>那组用例走 <c>AdminAppFactory</c>(内核 TestHost),
    /// 而 <c>AddTenonAdminWorkflow</c> 只在 <c>TenonAdmin.WorkflowTestHost</c> 里调用,所以它从**未**扫到过
    /// 工作流的种子。真正的守卫是启动期的 <c>DatabaseInitializer</c> 检查(下界 + 雪花地板 + 同实体唯一),
    /// 取号越界会在宿主启动时当场抛。</para>
    /// </summary>
    private const long JobId = TenonSeedIds.ConsumerMin + 47_000;

    /// <summary>
    /// <b>刻意 false(与本包的菜单种子相反),照 <c>DefaultJobSeed</c> 的留档理由:</b>job 行是运行态
    /// 可变数据——<c>NextRunTime</c>/计数器/用户改过的 cron 全在同一行,升级刷回种子值 = 清空运行态 +
    /// 吞掉用户调参。菜单是纯结构件才敢开 <c>SyncOnUpgrade</c>。
    /// </summary>
    public bool SyncOnUpgrade => false;

    public IEnumerable<SysJob> HasData() =>
    [
        new SysJob
        {
            Id = JobId,
            Code = TIMEOUT_SCAN_CODE,
            Name = "流程超时扫描",
            HandlerKind = JobHandlerKind.Compiled,
            HandlerName = typeof(WfTimeoutJob).FullName!,   // IAdminJob.Name 默认 = 类型全名

            TriggerKind = JobTriggerKind.Cron,
            // 每 5 分钟一拍(已是归一化 6 段)。刻意不是每分钟:超时的最小配置单位是**小时**
            // (WfTimeout.Hours 是 int),5 分钟分辨率下无任何可观测差异,而每分钟是 12 倍无谓查询。
            CronExpression = "0 */5 * * * ?",
            MisfireStrategy = JobMisfireStrategy.Skip,
            // 扫描是幂等的,但上一拍没跑完就叠第二拍只会互相 CAS 失败刷日志。
            ConcurrencyMode = JobConcurrencyMode.SerialSkip,
            Status = JobStatus.Ready,
            // NextRunTime 留空:调度器重载时按触发配置补算(种子编写期没有时钟)。
            TimeoutSeconds = 300,
            // 卫星包的任务,消费者应能停用/改节奏;IsSystem 留给内核自己的任务(它会禁删,47014)。
            IsSystem = false,
            AlertByNotice = true,
            Remark = "扫描 wf_task.DueTime 到期的待办,按节点 timeout.action 提醒 / 自动通过 / 自动拒绝 / 转办",
        },
    ];
}
