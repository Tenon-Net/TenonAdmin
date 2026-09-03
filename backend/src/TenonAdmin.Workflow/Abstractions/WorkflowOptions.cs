namespace TenonAdmin.Workflow;

/// <summary>
/// 工作流全局配置(对应 <c>TenonAdmin:Workflow</c>)。
/// 节点 / 流程级配置可覆盖此处默认值(空审批人策略等,见设计方案)。
/// </summary>
public sealed class WorkflowOptions
{
    /// <summary>自动节点未配置时的内置安全总尝试次数(含首次执行)。</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>自动节点总尝试次数允许的最小值。</summary>
    public const int MinMaxAttempts = 1;

    /// <summary>自动节点总尝试次数允许的最大值，防止配置制造近似无限重试。</summary>
    public const int MaxMaxAttempts = 100;

    /// <summary>节点执行 worker 每拍最多检视的 execution 数。</summary>
    public const int DefaultNodeExecutionScanBatchSize = 20;

    /// <summary>节点执行 worker 扫描批量允许的最大值。</summary>
    public const int MaxNodeExecutionScanBatchSize = 1000;

    /// <summary>节点执行租约默认时长(秒)。</summary>
    public const int DefaultNodeExecutionLeaseSeconds = 300;

    /// <summary>节点执行租约允许的最大时长(秒)。</summary>
    public const int MaxNodeExecutionLeaseSeconds = 3600;

    /// <summary>
    /// 自动节点全局默认总尝试次数(含首次执行);节点 <c>props.maxAttempts</c> 可覆盖。
    /// 对应 <c>TenonAdmin:Workflow:MaxAttempts</c>，绑定期必须在
    /// <see cref="MinMaxAttempts"/> 与 <see cref="MaxMaxAttempts"/> 之间。
    /// </summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>
    /// 节点执行 worker 每拍最多扫描的 execution 数，对应
    /// <c>TenonAdmin:Workflow:NodeExecutionScanBatchSize</c>。
    /// </summary>
    public int NodeExecutionScanBatchSize { get; set; } = DefaultNodeExecutionScanBatchSize;

    /// <summary>
    /// 节点 execution 单次领取租约时长(秒)，对应
    /// <c>TenonAdmin:Workflow:NodeExecutionLeaseSeconds</c>。
    /// </summary>
    public int NodeExecutionLeaseSeconds { get; set; } = DefaultNodeExecutionLeaseSeconds;

    /// <summary>
    /// 空审批人全局默认策略:<c>autoPass</c>(自动通过,出厂默认) /
    /// <c>transfer</c>(转指定人) / <c>block</c>(卡住并通知管理员)。
    /// </summary>
    public string Nobody { get; set; } = "autoPass";

    /// <summary>
    /// <see cref="WfTimeoutJob"/> 单次扫描的**处理**上限。每条要开一个引擎事务(读实例/版本 + CAS +
    /// 写 2–4 行),故比纯删除型任务保守;没处理完的下一拍继续,扫描按 <c>DueTime</c> 升序,最久的先处理。
    /// <para><b>这是「处理」预算而不是「取回行数」上限。</b>到期窗口里天然混着这一拍推不动的行——被防刷
    /// 间隔挡下的提醒(提醒不清 <c>DueTime</c>,那是「不改状态」契约的推论)最典型。若把它们也算进预算,
    /// 升序 + 永不消费 = 队头永久堵塞,更新的自动通过/拒绝/转办永远排不进队,而 Job 照样返回 Success。
    /// 故扫描按 <c>(DueTime, Id)</c> 游标翻页,只有真推动了的行才扣预算;翻页天花板见
    /// <c>WfTimeoutJob.MaxScanRounds</c>。</para>
    /// </summary>
    public int TimeoutScanBatchSize { get; set; } = 200;

    /// <summary>
    /// <see cref="WfTimeoutAction.Remind"/> 的最小提醒间隔(小时)。
    /// <c>0</c> = 跟随节点自己的 <see cref="WfTimeout.Hours"/>(下限 1 小时)——「配 24 小时超时的节点每
    /// 24 小时催一次」,不引入第二个要理解的旋钮。契约只写了「可重复触发」没写节奏,按字面实现的话
    /// 一件逾期三天的待办在 5 分钟一拍下会被提醒 864 次。
    /// <para>需要别的节奏(如只提醒一次)覆写 <see cref="WfTimeoutJob.ShouldRemindAsync"/>——但**光覆写
    /// 不生效**:调度器按 <c>sys_job.HandlerName</c> 解析处理器,种子写死的是基类全名,子类必须同时改那一行
    /// (后台可直接改)或自己覆写 <c>Name</c> 并前置注册。完整说明见 <see cref="WfTimeoutJob"/> 类级注释。</para>
    /// </summary>
    public int TimeoutRemindMinIntervalHours { get; set; }

    internal static bool IsValidMaxAttempts(int value) =>
        value is >= MinMaxAttempts and <= MaxMaxAttempts;
}

internal static class WorkflowOptionsValidation
{
    public static void Validate(WorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!WorkflowOptions.IsValidMaxAttempts(options.MaxAttempts))
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:MaxAttempts 配置无效:值为 {options.MaxAttempts}," +
                $"必须在 {WorkflowOptions.MinMaxAttempts}–{WorkflowOptions.MaxMaxAttempts} 之间。");
        }

        if (options.NodeExecutionScanBatchSize is < 1 or > WorkflowOptions.MaxNodeExecutionScanBatchSize)
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:NodeExecutionScanBatchSize 配置无效:值为 {options.NodeExecutionScanBatchSize}," +
                $"必须在 1–{WorkflowOptions.MaxNodeExecutionScanBatchSize} 之间。");
        }

        if (options.NodeExecutionLeaseSeconds is < 1 or > WorkflowOptions.MaxNodeExecutionLeaseSeconds)
        {
            throw new InvalidOperationException(
                $"TenonAdmin:Workflow:NodeExecutionLeaseSeconds 配置无效:值为 {options.NodeExecutionLeaseSeconds}," +
                $"必须在 1–{WorkflowOptions.MaxNodeExecutionLeaseSeconds} 秒之间。");
        }
    }
}
