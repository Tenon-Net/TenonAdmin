using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 节点执行 attempt 记录(<c>wf_node_execution_attempt</c>,M3a-1 Task 4)——append-only:每次真实调用一行,
/// 重试新增、不覆盖旧 attempt(AI 基石 §4.5「attempt 必须保留每次真实调用」)。基类带来的
/// <see cref="AuditEntity.UpdateTime"/>/<c>UpdateUserId</c>/<see cref="ISoftDelete.IsDelete"/> <b>永不置真</b>,
/// 清理走保留期策略而非普通软删除(评审 §4.7);<see cref="WfNodeExecutionAttemptStore"/> 只暴露
/// <see cref="WfNodeExecutionAttemptStore.AppendAsync"/>,不提供更新/删除——与 <see cref="WfHistory"/>/
/// <see cref="WfOperationReceipt"/> 同源。
/// <para><b>刻意继承 <see cref="BaseEntity"/> 而非 <c>DataEntity</c></b>。<c>DataEntity</c> 带
/// <see cref="IOrgScoped"/> 全局数据范围过滤器(只作用于 SELECT),而本表的读写方是<b>没有 HTTP 请求上下文的
/// 后台 worker</b>——<c>IDataScopeContext</c> 为空会让查询静默返回 0 行,症状伪装成「调度器扫不到活」而不是
/// 报错,与 <see cref="WfNodeExecution"/> 同源。<b>本表不带 <c>ScopeKey</c> 列</b>:attempt 行永远经
/// <see cref="ExecutionId"/> 到达,父行 <c>WfNodeExecution.ScopeKey</c> 已承载机构维度;没有查询要用它,
/// 反规范化只会多一个必须与父行保持一致的写入点。</para>
/// <para><b><see cref="AttemptNo"/> 1 基,= 领取读回后的 <c>WfNodeExecution.AttemptCount</c>,写入时不得再 +1</b>。
/// 三处口径(领取 UPDATE 的 <c>AttemptCount + 1</c> / <c>WfNodeExecutionContext.Attempt</c> / 本列)必须对齐,
/// 这是经典的差一陷阱——签名与实现都刻意不留「调用方自己传 attempt 号」的入口(见 Store)。</para>
/// <para><b>两个业务时间列全部 UTC,列名一律带 <c>Utc</c> 后缀</b>(<see cref="StartedAtUtc"/>/
/// <see cref="EndedAtUtc"/>),值由调用方算好传入。<b>硬约束</b>:基类审计列 <see cref="AuditEntity.CreateTime"/>/
/// <see cref="AuditEntity.UpdateTime"/> 仍是 local(AOP 填的),<b>任何代码都不得把它们与任何 <c>*Utc</c> 列
/// 做比较或相减</b>。</para>
/// <para><b>全表一列都不写 <c>DefaultValue</c></b>:<c>DefaultValue</c> 唯一的作用是让
/// <c>DbMaintenanceProvider.AddColumn</c> 走「先加可空列 → 回填 → 改 NOT NULL」三步序列,
/// <b><c>CREATE TABLE</c> 路径根本不读它</b>;本表是本 Task 新建表,没有「存量行升级」这回事,写它只是噪音。
/// Task 1 那条「非空列必须带 <c>DefaultValue</c>」契约管的是加列,不是建表。</para>
/// <para><b>不存输出正文</b>,只存 <see cref="OutputHash"/> + 512 截断摘要(§6.2「输出正文、敏感字段和密钥不
/// 直接进入日志」)。§6.2 列的 <c>Provider</c>/<c>Model</c>/<c>PromptVersion</c>/<c>SchemaVersion</c>/
/// <c>PolicyVersion</c> 归 §七 <c>wf_ai_decision</c>(同一事实不设两个家);<c>TokenUsage</c>/<c>Cost</c> 待
/// M3b 以可空列 <c>ADD COLUMN</c> 补(四库都接受,<c>WfHistory.RequestId</c> 先例)。</para>
/// <para><b>崩溃可见性</b>(顺带得到的性质):<c>execution.AttemptCount − count(attempt)</c> = 领了但没返回的
/// 次数,即崩溃/被杀次数。<see cref="EndedAtUtc"/> 非空正是为了保住这个口径——一行 attempt = 一次<b>已返回</b>
/// 的调用,Task 7 的崩溃恢复会用到这个观测点。</para>
/// </summary>
[SugarTable("wf_node_execution_attempt", TableDescription = "节点执行 attempt 记录")]
[SugarIndex("uk_wf_node_exec_attempt_no",
    nameof(ExecutionId), OrderByType.Asc,
    nameof(AttemptNo), OrderByType.Asc,
    IsUnique = true)]
public class WfNodeExecutionAttempt : BaseEntity
{
    /// <summary>所属 <c>wf_node_execution.Id</c>。本仓无 DB 外键先例,靠唯一索引首列串联。</summary>
    [SugarColumn(ColumnDescription = "所属执行记录 Id")]
    public long ExecutionId { get; set; }

    /// <summary>本次 attempt 序号,1 基;= 领取读回后的 <c>execution.AttemptCount</c>,写入时不得再 +1。</summary>
    [SugarColumn(ColumnDescription = "attempt 序号(1 基)")]
    public int AttemptNo { get; set; }

    [SugarColumn(ColumnDescription = "开始时刻(UTC)")]
    public DateTime StartedAtUtc { get; set; }

    /// <summary>结束时刻(UTC)。非空:一行 = 一次已返回的调用(见类注释「崩溃可见性」)。</summary>
    [SugarColumn(ColumnDescription = "结束时刻(UTC)")]
    public DateTime EndedAtUtc { get; set; }

    [SugarColumn(ColumnDescription = "本次 attempt 的结果类型")]
    public WfNodeExecutionResultType ResultType { get; set; }

    /// <summary>成功时的输出摘要(已截断至 512);失败/回退时为 null。</summary>
    [SugarColumn(Length = WfNodeExecutionAttemptStore.SummaryMaxLength, IsNullable = true, ColumnDescription = "输出摘要")]
    public string? OutputSummary { get; set; }

    /// <summary>输出正文的 SHA-256 小写 hex;正文本身不落库(§6.2)。<c>OutputJson == null</c> → 本列 null。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "输出哈希(SHA-256 小写 hex)")]
    public string? OutputHash { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "失败错误码")]
    public int? ErrorCode { get; set; }

    /// <summary>失败/回退时的错误摘要(已截断至 512);成功时为 null。</summary>
    [SugarColumn(Length = WfNodeExecutionAttemptStore.SummaryMaxLength, IsNullable = true, ColumnDescription = "错误摘要")]
    public string? ErrorSummary { get; set; }
}
