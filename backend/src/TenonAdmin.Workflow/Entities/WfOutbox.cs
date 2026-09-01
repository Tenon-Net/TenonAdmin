using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 可靠派发外发信箱(<c>wf_outbox</c>,M3a-1 Task 5)——execution 结果回写的同一短事务里入队待投递消息
/// (§4.6 步骤 5),供进程外消费方(HTTP/MQ 等)拉取投递。本 Task 只交付
/// <see cref="WfOutboxStore.EnqueueAsync"/>(写得进去、状态可查询);领取/重投/退避/终态四条状态边、
/// 后台扫描 job 归消费者任务(见 <see cref="WfOutboxStatus"/> 的状态图)。
/// <para><b>刻意继承 <see cref="BaseEntity"/> 而非 <c>DataEntity</c></b>。<c>DataEntity</c> 带
/// <see cref="IOrgScoped"/> 全局数据范围过滤器(只作用于 SELECT),而本表的读写方是<b>没有 HTTP 请求上下文的
/// 后台 worker</b>——<c>IDataScopeContext</c> 为空会让扫描静默返回 0 行,症状伪装成「消息永远不投递」而不是
/// 报错,与 <see cref="WfNodeExecution"/>/<see cref="WfNodeExecutionAttempt"/> 同源。<see cref="ISoftDelete.IsDelete"/>
/// 永不置真,清理走保留期策略(<see cref="CompletedAtUtc"/> 是那把钩子)。</para>
/// <para><b>不带 <c>ScopeKey</c> 列</b>(与 attempt 同):扫描维度是 <c>(Status, AvailableAtUtc)</c> 全局队列,
/// 投递本身不需要机构维度;需要时经 <see cref="ExecutionId"/> 到父行取。反规范化只会多一个必须与父行保持
/// 一致的写入点。</para>
/// <para><b>两个业务时间列全部 UTC,列名一律带 <c>Utc</c> 后缀</b>(<see cref="AvailableAtUtc"/>/
/// <see cref="CompletedAtUtc"/>),值由调用方算好传入。<b>硬约束</b>:基类审计列
/// <see cref="AuditEntity.CreateTime"/>/<see cref="AuditEntity.UpdateTime"/> 仍是 local(AOP 填的),
/// <b>任何代码都不得把它们与任何 <c>*Utc</c> 列做比较或相减</b>。</para>
/// <para><b>全表一列都不写 <c>DefaultValue</c></b>:<c>DefaultValue</c> 唯一的作用是让
/// <c>DbMaintenanceProvider.AddColumn</c> 走「先加可空列 → 回填 → 改 NOT NULL」三步序列,
/// <b><c>CREATE TABLE</c> 路径根本不读它</b>;本表是本 Task 新建表,没有「存量行升级」这回事,写它只是噪音。
/// Task 1 那条「非空列必须带 <c>DefaultValue</c>」契约管的是加列,不是建表。</para>
/// <para><b><see cref="AttemptCount"/> 就是 fence</b>:本表刻意不设 <c>LeaseOwner</c>/<c>LeaseExpiresAtUtc</c>/
/// <c>Fence</c> 三列——<see cref="AvailableAtUtc"/> 一列兼任「下次可投时刻」与「租约到期」(领取 = 推到未来),
/// <see cref="AttemptCount"/>(每次领取 +1、单调、已在表里)挡住老 owner 的迟到回写。消费者任务的领取回写
/// <b>必须</b>带 <c>WHERE AttemptCount = @myAttemptCount</c> 的 CAS——不要再造一个 <c>Fence</c> 列。</para>
/// <para><see cref="AvailableAtUtc"/> <b>非空</b>(刻意偏离 <see cref="WfNodeExecution.NextRetryAtUtc"/> 可空的
/// 先例):它同时承载「何时可重试」与「租约到期」两件事,非空让领取谓词退化成一次简单比较,天然绕开 SQL
/// 三值逻辑陷阱(<c>NULL</c> 永不可领)。入队时 <c>AvailableAtUtc = nowUtc</c>(立即可投)。</para>
/// <para><b>正文存全文</b>:<see cref="PayloadJson"/> 走 <see cref="StaticConfig.CodeFirst_BigString"/>,
/// <b>不截断</b>(截断 JSON = 损坏消息)。outbox 是给机器读的传输记录,消费方在另一进程、可能崩溃很久之后
/// 才读这一行,只有正文能回答「我该发什么出去」;这与 attempt 表只存摘要+hash 的取舍方向相反且必须如此
/// (两张表回答不同问题)。脱敏责任在<b>生产者</b>(入队方决定什么进消息),不在本表。<b>禁止</b>改成裸
/// <c>ColumnDataType = "text"</c>——SqlServer 上非 Unicode,中文读回变 <c>???</c>(nightly #25 先例)。</para>
/// <para><see cref="LastError"/> 写入方必须在 C# 侧截断到 512(外部错误文本是 trust boundary),本轮零写入点,
/// 责任交代给消费者任务。<see cref="MessageKey"/> 的天花板:一个 <c>(execution, MessageType)</c> 只能有一条
/// 消息;同一 execution 需要发两条同类型消息时,升级路径是在末尾追加 discriminator 段并给旧维度定哨兵。
/// <see cref="LastError"/>/<see cref="CompletedAtUtc"/>/<see cref="WfOutboxStatus.Dispatching"/>/
/// <see cref="WfOutboxStatus.Dispatched"/>/<see cref="WfOutboxStatus.Failed"/> 本轮零写入点,只保证列存在、
/// 可读回。</para>
/// </summary>
[SugarTable("wf_outbox", TableDescription = "可靠派发外发信箱")]
[SugarIndex("uk_wf_outbox_message_key", nameof(MessageKey), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_wf_outbox_scan", nameof(Status), OrderByType.Asc, nameof(AvailableAtUtc), OrderByType.Asc)]
public class WfOutbox : BaseEntity
{
    /// <summary>所属 <c>wf_node_execution.Id</c>。本仓无 DB 外键先例,靠 <see cref="MessageKey"/> 前缀串联。</summary>
    [SugarColumn(ColumnDescription = "所属执行记录 Id")]
    public long ExecutionId { get; set; }

    /// <summary>
    /// 给进程外消费方的消息契约名。<b>刻意是 <c>string</c> 不是枚举</b>:它是给消费者看的线上契约,消费者
    /// 要发自己的消息类型,枚举是封闭的、消费者加不了成员而不 fork——与本仓可替换性第一原则冲突。内核已知
    /// 取值见 <see cref="WfOutboxStore"/> 上的 <c>const string</c>。不得含 <c>':'</c>(它是 <see cref="MessageKey"/>
    /// 的分隔符)。
    /// </summary>
    [SugarColumn(Length = 64, ColumnDescription = "消息契约类型")]
    public string MessageType { get; set; } = "";

    /// <summary>
    /// <c>{ExecutionKey}:{MessageType}</c>,唯一索引建在本列——消费方去重就靠它。与
    /// <see cref="WfNodeExecution.ExecutionKey"/> 的关系是<b>派生</b>,不是复用也不是独立生成:复用会让同一
    /// execution 的第二种 <see cref="MessageType"/> 撞唯一键、被 ensure-insert 静默吞掉;独立生成会让崩溃
    /// 恢复重放产出同一条消息的两个不同 key,消费方去重当场失效。
    /// </summary>
    [SugarColumn(Length = 128, ColumnDescription = "幂等消息键({ExecutionKey}:{MessageType})")]
    public string MessageKey { get; set; } = "";

    /// <summary>
    /// 待投递正文全文,<b>不截断</b>(见类注释)。<c>null</c> = 无正文的消息。
    /// </summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "待投递正文 JSON")]
    public string? PayloadJson { get; set; }

    [SugarColumn(ColumnDescription = "外发状态")]
    public WfOutboxStatus Status { get; set; } = WfOutboxStatus.Pending;

    /// <summary>已领取次数,从 0 起;<b>它就是 fence</b>——消费者回写必须 <c>WHERE AttemptCount = @myAttemptCount</c>。</summary>
    [SugarColumn(ColumnDescription = "已领取次数")]
    public int AttemptCount { get; set; }

    /// <summary>
    /// 兼任「下次可投时刻」与「租约到期」(UTC);非空(见类注释)。入队 = <c>nowUtc</c>(立即可投)。
    /// </summary>
    [SugarColumn(ColumnDescription = "下次可投/租约到期时刻(UTC)")]
    public DateTime AvailableAtUtc { get; set; }

    /// <summary>最近一次投递失败摘要;写入方必须 C# 侧截断到 512。本轮零写入点。</summary>
    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "最近一次投递失败摘要")]
    public string? LastError { get; set; }

    /// <summary>进入 <see cref="WfOutboxStatus.Dispatched"/>/<see cref="WfOutboxStatus.Failed"/> 终态的时刻(UTC);保留期清理作业的钩子。本轮零写入点。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "终态时刻(UTC)")]
    public DateTime? CompletedAtUtc { get; set; }
}
