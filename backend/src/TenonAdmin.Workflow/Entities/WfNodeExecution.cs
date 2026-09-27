using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 节点可靠执行记录(<c>wf_node_execution</c>,M3a-1 Task 3)——记录「某个 token 在某次访问某节点」的
/// 一次可靠执行:领取(lease/fence)、重试预算、handler 身份与幂等哈希。Task 8b 通过
/// <see cref="WfNodeExecutionStore"/> 接入既有调度器，并由 dispatcher 写入 attempt/outbox；本实体仍只承载
/// execution 的持久化事实，不复制 worker 或结果状态机。
/// <para><b>刻意继承 <see cref="BaseEntity"/> 而非 <c>DataEntity</c></b>。<c>DataEntity</c> 带
/// <see cref="IOrgScoped"/> 全局数据范围过滤器(只作用于 SELECT),而本表的读写方是<b>没有 HTTP 请求上下文的
/// 后台 worker</b>——<c>IDataScopeContext</c> 是空的,<c>IOrgScoped</c> 过滤器会让扫描直接返回 0 行,症状是
/// 「调度器永远扫不到活干」而不是报错。机构维度改由本表显式非空的 <see cref="ScopeKey"/> 承载,与
/// <see cref="WfOperationReceipt"/> 同源。</para>
/// <para><b>全表所有列一律不写 <c>DefaultValue</c>。</b><c>DefaultValue</c> 唯一的作用是让
/// <c>DbMaintenanceProvider.AddColumn</c> 走「先加可空列 → 回填 → 改 NOT NULL」三步序列(见
/// <see cref="WfInstance.Version"/> 注释里反编译核实的机制);<b><c>CREATE TABLE</c> 路径根本不读它</b>。
/// 本表是本 Task 新建表,没有「存量行升级」这回事,写 <c>DefaultValue</c> 只是噪音。</para>
/// <para><b>四个业务时间列全部 UTC,列名一律带 <c>Utc</c> 后缀</b>
/// (<see cref="DeadlineAtUtc"/>/<see cref="NextRetryAtUtc"/>/<see cref="LeaseExpiresAtUtc"/>/
/// <see cref="CompletedTimeUtc"/>),值由调用方算好传入。这是<b>刻意偏离</b>本仓「持久化业务时间戳走
/// <c>GetLocalNow().DateTime</c>」的惯例——列名后缀就是唯一的护栏。<b>硬约束</b>:基类审计列
/// <see cref="AuditEntity.CreateTime"/>/<see cref="AuditEntity.UpdateTime"/> 仍是 local(AOP 填的),
/// <b>任何代码都不得把它们与 <c>*Utc</c> 列做比较或相减</b>。</para>
/// <para>凡注释标「建表期预留、本轮零写入点」的列(<see cref="DeadlineAtUtc"/>/<see cref="HandlerType"/>/
/// <see cref="HandlerVersion"/>/<see cref="InputHash"/>/<see cref="OutputHash"/>/<see cref="CompletedTimeUtc"/>/
/// <see cref="ErrorCode"/>/<see cref="Summary"/>,共 8 列)都是<b>建表期一次造齐</b>的预留列:全部可空。
/// 其中 <see cref="HandlerType"/>/<see cref="CompletedTimeUtc"/>/<see cref="ErrorCode"/>/<see cref="Summary"/>
/// 4 列已由 Task 6 起接上写入点(<see cref="WorkflowEngine.ClaimExecutionWritebackAsync"/> 回写),余
/// <see cref="DeadlineAtUtc"/>/<see cref="HandlerVersion"/>/<see cref="InputHash"/>/<see cref="OutputHash"/>
/// 4 列仍零写入点。</para>
/// </summary>
[SugarTable("wf_node_execution", TableDescription = "节点可靠执行记录")]
[SugarIndex("uk_wf_node_exec_key", nameof(ExecutionKey), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_wf_node_exec_scan", nameof(Status), OrderByType.Asc, nameof(NextRetryAtUtc), OrderByType.Asc)]
public class WfNodeExecution : BaseEntity
{
    /// <summary>
    /// <see cref="WfExecutionKey.Compute"/> 算出的 64 位小写十六进制,唯一索引建在本列。
    /// 组成字段(<see cref="ScopeKey"/>/<see cref="InstanceId"/>/<see cref="TokenId"/>/
    /// <see cref="NodeVisitId"/>/<see cref="NodeId"/>/<see cref="DefinitionVersionId"/>)一并保留只为排查,
    /// 不参与唯一性——与 <see cref="WfOperationReceipt"/> 同款。
    /// </summary>
    [SugarColumn(Length = 64, ColumnDescription = "执行幂等键(SHA-256 小写 hex)")]
    public string ExecutionKey { get; set; } = "";

    /// <summary>
    /// 机构/租户范围键。非空——写入方必须用 <see cref="WfIdentityHash.NormalizeScopeKey"/> 的返回值落库
    /// (无机构 → 哨兵),不允许 null 与空串产生两个 identity,与 <see cref="WfOperationReceipt.ScopeKey"/> 同款。
    /// </summary>
    [SugarColumn(Length = 64, ColumnDescription = "机构/租户范围键(无机构用哨兵)")]
    public string ScopeKey { get; set; } = "";

    [SugarColumn(ColumnDescription = "流程实例 Id")]
    public long InstanceId { get; set; }

    [SugarColumn(ColumnDescription = "运行 token Id")]
    public long TokenId { get; set; }

    /// <summary>节点访问序号;可空(诊断列,不参与唯一性——唯一性靠 <see cref="ExecutionKey"/>)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "节点访问序号")]
    public long? NodeVisitId { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "节点 Id")]
    public string NodeId { get; set; } = "";

    [SugarColumn(ColumnDescription = "节点类型")]
    public WfNodeType NodeType { get; set; }

    [SugarColumn(ColumnDescription = "定义版本 Id")]
    public long DefinitionVersionId { get; set; }

    [SugarColumn(ColumnDescription = "执行状态")]
    public WfNodeExecutionStatus Status { get; set; } = WfNodeExecutionStatus.Pending;

    /// <summary>已领取次数;领取 UPDATE 里 +1,读回后即当次 <c>AttemptNo</c>(1 基,Task 4 对齐)。</summary>
    [SugarColumn(ColumnDescription = "已领取次数")]
    public int AttemptCount { get; set; }

    [SugarColumn(ColumnDescription = "最大执行次数(含首次)")]
    public int MaxAttempts { get; set; }

    /// <summary>下次可重试时刻(UTC);仅 <see cref="WfNodeExecutionStatus.RetryScheduled"/> 有意义。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "下次可重试时刻(UTC)")]
    public DateTime? NextRetryAtUtc { get; set; }

    /// <summary>执行截止时刻(UTC);建表期预留,本轮零写入点。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "执行截止时刻(UTC)")]
    public DateTime? DeadlineAtUtc { get; set; }

    /// <summary>
    /// 当前租约持有者标识;未领取 = <c>null</c> 而非空串。长度对齐 <c>SysJobLock.OwnerNodeName</c>。
    /// worker 标识由<b>调用方传参</b>——本 Task 不接 DI、不读 <c>AdminJobsOptions</c>。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "租约持有者")]
    public string? LeaseOwner { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "租约到期时刻(UTC)")]
    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>
    /// 领取令牌,从 0 起,首次领取变 1。用 <c>long</c> 不用 <c>int</c>——Task 5/8 会把它当幂等/排序令牌交给
    /// 外部系统。<c>Running → Running</c> 重新领取时 +1,老 owner 的回写靠它被拒。
    /// </summary>
    [SugarColumn(ColumnDescription = "领取令牌(fence)")]
    public long Fence { get; set; }

    /// <summary>Handler 类型标识;Task 6 起由 <see cref="WorkflowEngine.ClaimExecutionWritebackAsync"/> 回写。</summary>
    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "Handler 类型标识")]
    public string? HandlerType { get; set; }

    /// <summary>Handler 版本;建表期预留,本轮零写入点。</summary>
    [SugarColumn(Length = 32, IsNullable = true, ColumnDescription = "Handler 版本")]
    public string? HandlerVersion { get; set; }

    /// <summary>入参哈希;建表期预留,本轮零写入点。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "入参哈希")]
    public string? InputHash { get; set; }

    /// <summary>出参哈希;建表期预留,本轮零写入点。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "出参哈希")]
    public string? OutputHash { get; set; }

    /// <summary>完成时刻(UTC);Task 6 起由 <see cref="WorkflowEngine.ClaimExecutionWritebackAsync"/> 回写(仅终态分支写入)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "完成时刻(UTC)")]
    public DateTime? CompletedTimeUtc { get; set; }

    /// <summary>失败错误码;Task 6 起由 <see cref="WorkflowEngine.ClaimExecutionWritebackAsync"/> 回写(仅终态分支写入)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "失败错误码")]
    public int? ErrorCode { get; set; }

    /// <summary>诊断摘要;Task 6 起由 <see cref="WorkflowEngine.ClaimExecutionWritebackAsync"/> 回写(仅终态分支写入)。</summary>
    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "诊断摘要")]
    public string? Summary { get; set; }
}
