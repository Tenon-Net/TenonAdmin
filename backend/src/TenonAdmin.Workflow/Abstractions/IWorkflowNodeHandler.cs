namespace TenonAdmin.Workflow;

/// <summary>
/// 节点执行结果类型(M3a-1)。数值将进数据库评审 §6.2 <c>wf_node_execution_attempt.ResultType</c> 列,
/// <b>只追加不重排</b>(同 <see cref="WfHistoryActorType"/> 的既有约定)。
/// <para><b>刻意无 0 值</b>:0 空缺意味着 <c>default(WfNodeExecutionResultType)</c> 非法,dispatcher
/// <c>switch</c> 的 <c>default:</c> 臂应抛异常,杜绝「零初始化悄悄等于成功」。</para>
/// <para><b>刻意没有 <c>Cancelled</c> 成员</b>:取消在 .NET 里已有自带通道——<see cref="CancellationToken"/>
/// + <see cref="OperationCanceledException"/>。语义方向也相反:<see cref="TerminalFailure"/> =「永远做不成,
/// 不许重试」;取消 =「这次没跑完,应该被重新领取」(Task 7 崩溃恢复覆盖的那条路)。加一个 <c>Cancelled</c>
/// 返回值等于给同一件事开两条路,不做。「实例被外部撤销」也不在此列——handler 压根不知道,是 dispatcher
/// 在回写短事务里靠 fence/CAS 发现并丢弃结果的。</para>
/// <para>本类型是 handler 每次 attempt 的答复,与 Task 3 的 <c>WfNodeExecutionStatus</c>(行状态机)是
/// <b>两个不同类型</b>,不许合并——一次 execution 可有多个 attempt,每个 attempt 一个结果,行状态是它们的聚合。</para>
/// </summary>
public enum WfNodeExecutionResultType
{
    Succeeded = 1,
    RetryableFailure = 2,
    ManualFallback = 3,
    TerminalFailure = 4,
}

/// <summary>
/// 一次节点执行(attempt)的结果——私有构造 + 静态工厂,只能通过下列四个工厂之一构造,
/// 保证 <see cref="Type"/> 与实际语义不会互相矛盾(如 <see cref="Succeeded"/> 却带 <see cref="ErrorCode"/>)。
/// <para><b>不可整体反序列化</b>(私有构造 → <c>System.Text.Json</c> 反序列化不了),这是有意的:
/// 本类型从不整体持久化,只投影进 §6.2 <c>wf_node_execution_attempt</c> 的四个扁平列
/// (<c>ResultType</c>/<c>OutputSummary</c>/<c>ErrorCode</c>/<c>ErrorSummary</c>)。</para>
/// </summary>
public sealed class WfNodeExecutionResult
{
    private WfNodeExecutionResult()
    {
    }

    public required WfNodeExecutionResultType Type { get; init; }

    /// <summary>节点输出;仅 <see cref="WfNodeExecutionResultType.Succeeded"/> 有意义。</summary>
    public string? OutputJson { get; init; }

    /// <summary>落 attempt 的 <c>OutputSummary</c>(成功时)或 <c>ErrorSummary</c>(失败/回退时)。</summary>
    public string? Summary { get; init; }

    /// <summary>失败/回退时的错误码(48xxx 或 handler 自有码)。</summary>
    public int? ErrorCode { get; init; }

    /// <summary>仅 <see cref="WfNodeExecutionResultType.RetryableFailure"/> 有意义;<c>null</c> = 由 dispatcher 退避策略决定。</summary>
    public TimeSpan? RetryAfter { get; init; }

    public static WfNodeExecutionResult Succeeded(string? outputJson = null, string? summary = null) => new()
    {
        Type = WfNodeExecutionResultType.Succeeded,
        OutputJson = outputJson,
        Summary = summary,
    };

    public static WfNodeExecutionResult RetryableFailure(int? errorCode = null, string? summary = null, TimeSpan? retryAfter = null) => new()
    {
        Type = WfNodeExecutionResultType.RetryableFailure,
        ErrorCode = errorCode,
        Summary = summary,
        RetryAfter = retryAfter,
    };

    public static WfNodeExecutionResult ManualFallback(int? errorCode = null, string? summary = null) => new()
    {
        Type = WfNodeExecutionResultType.ManualFallback,
        ErrorCode = errorCode,
        Summary = summary,
    };

    public static WfNodeExecutionResult TerminalFailure(int? errorCode = null, string? summary = null) => new()
    {
        Type = WfNodeExecutionResultType.TerminalFailure,
        ErrorCode = errorCode,
        Summary = summary,
    };
}

/// <summary>
/// 一次节点执行的输入上下文(M3a-1)——投影自 <see cref="WfInstance"/>/<see cref="WfToken"/>/<see cref="WfNode"/>,
/// <b>不含 SqlSugar 实体本身、不含 <c>ISqlSugarClient</c></b>(硬约束,有结构化断言守着)。
/// <para><see cref="VariablesJson"/> 原样透传前端发起时提交的摘要变量,后端从不校验——措辞与语义对齐
/// <see cref="IWfConditionEvaluator"/>:实现必须对烂 JSON 免疫。</para>
/// <para><see cref="NodeProps"/> 是 dispatcher 自己反序列化出的快照实例,handler 只读;<b>dispatcher 不得
/// 把引擎内部那棵活树(<c>ctx.Model</c>)上的节点对象直接递进来</b>——<see cref="WfNodeProps"/> setter 可写,
/// 与「不可变快照」字面不符,这条纪律是唯一的保障,靠代码审查兜住,不是类型系统强制。</para>
/// <para><see cref="Attempt"/> 1 基:= 即将写入的 <c>wf_node_execution_attempt.AttemptNo</c> = 领取时
/// <c>AttemptCount + 1</c>(三处口径必须对齐)。</para>
/// <para><see cref="ExecutionKey"/> 是不透明值,其构成由 Task 3 定义,本类型不对其形状做任何假设。</para>
/// </summary>
public sealed class WfNodeExecutionContext
{
    public required string ExecutionKey { get; init; }

    public required long InstanceId { get; init; }

    public required long TokenId { get; init; }

    public long? NodeVisitId { get; init; }

    public required string NodeId { get; init; }

    public required WfNodeType NodeType { get; init; }

    public required long DefinitionVersionId { get; init; }

    /// <summary>数据范围锚点;本仓没有 tenant 原语,租户维度就是 <c>WfInstance</c> 的 <c>CreateOrgId</c>。</summary>
    public long? OrgId { get; init; }

    public required long StarterUserId { get; init; }

    public string? BusinessKey { get; init; }

    public WfNodeProps? NodeProps { get; init; }

    public string? VariablesJson { get; init; }

    public required int Attempt { get; init; }

    /// <summary>
    /// 绝对截止时刻(非相对超时——相对值在「领取 → 排队 → 真正开跑」之间会失真)。
    /// <see cref="DateTimeOffset"/> 而非 <see cref="DateTime"/>:从类型上消灭 Kind 歧义。
    /// handler 要相对超时自己算 <c>DeadlineAtUtc - TimeProvider.GetUtcNow()</c>。
    /// </summary>
    public required DateTimeOffset DeadlineAtUtc { get; init; }
}

/// <summary>
/// 工作流节点执行 SPI(M3a-1):按 <see cref="WfNode.Type"/> 分发到具体实现,一种节点类型一个实现。
/// <para>
/// 注册路径同 <see cref="Core.IAdminJob"/>:<c>services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IWorkflowNodeHandler, MyHandler&gt;())</c>;
/// dispatcher 用 <c>GetServices&lt;IWorkflowNodeHandler&gt;().FirstOrDefault(h => h.NodeType == node.Type)</c> 挑实现——不用 keyed DI,
/// 不新发明范式。键用 <see cref="WfNodeType"/> 枚举而非字符串:对端 <c>WfNode.Type</c> 本来就是枚举,用字符串
/// 等于强插一次 camelCase 往返,漂移只会在运行时表现为「找不到 handler」。
/// </para>
/// <para>
/// <b>handler 不得推进 token、不得写任务状态、不得自开数据库事务</b>(AI 基石 §4.5/§4.7)——只返回结果,
/// 由 dispatcher 在短事务里落地。
/// </para>
/// </summary>
public interface IWorkflowNodeHandler
{
    WfNodeType NodeType { get; }

    Task<WfNodeExecutionResult> ExecuteAsync(WfNodeExecutionContext context, CancellationToken cancellationToken);
}
