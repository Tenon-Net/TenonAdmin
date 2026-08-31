using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程实例(<c>wf_instance</c>)——一次发起的运行单;机构隔离,锚点即 <c>CreateOrgId</c>。
/// 业务单据在消费者自己的表,经 <see cref="BusinessKey"/> 关联;摘要变量进 <see cref="VariablesJson"/>。
/// </summary>
[SugarTable("wf_instance", TableDescription = "流程实例")]
[SugarIndex("idx_wf_instance_def_ver", nameof(DefinitionVersionId), OrderByType.Asc)]
[SugarIndex("idx_wf_instance_starter", nameof(StarterUserId), OrderByType.Asc)]
[SugarIndex("idx_wf_instance_status", nameof(Status), OrderByType.Asc)]
[SugarIndex("idx_wf_instance_biz_key", nameof(BusinessKey), OrderByType.Asc)]
public class WfInstance : DataEntity
{
    [SugarColumn(ColumnDescription = "定义版本 Id")]
    public long DefinitionVersionId { get; set; }

    /// <summary>业务单据键(消费者表主键或业务号;可空=纯审批无业务挂载)。</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "业务键")]
    public string? BusinessKey { get; set; }

    [SugarColumn(ColumnDescription = "发起人用户 Id")]
    public long StarterUserId { get; set; }

    [SugarColumn(ColumnDescription = "状态(1 运行 / 2 通过 / 3 拒绝 / 4 撤销 / 5 终止)")]
    public WfInstanceStatus Status { get; set; } = WfInstanceStatus.Running;

    /// <summary>
    /// 实例完结时间(数据库评审 §4.2)——进入 Approved/Rejected/Cancelled 时与 <see cref="Status"/>
    /// <b>同一条 UPDATE</b> 写入,唯一落点是 <see cref="WfExecutionContext.WriteInstanceTerminalStatusAsync"/>。
    /// <para><c>UpdateTime</c> 当不了完结时间:重提、修复、任何一次整行更新都会刷新它。</para>
    /// <para><b>刻意 nullable 且不给 <c>DefaultValue</c></b>:升级策略是「新增列先 nullable」(评审 §九 #2),
    /// 而 nullable 的 <c>ADD COLUMN</c> 四库都直接接受 —— <see cref="Version"/> 注释里那条「翻可空 → ADD COLUMN
    /// → 回填 → 改 NOT NULL」三步路<b>只对 NOT NULL 列</b>才走,这里写 <c>DefaultValue</c> 只会白跑一遍回填
    /// UPDATE。升级前就已终态的旧行由 <see cref="WfCompletedTimeBackfill"/> 从 <c>InstanceCompleted</c> 事件
    /// 回填,无事件可依据的<b>保持空</b>(评审 §九 #4)。</para>
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "完结时间")]
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 实例级乐观锁;每次<b>终态写入</b>经「期望状态 + 版本」双条件 CAS 递增
    /// (<see cref="WfExecutionContext.ClaimInstanceAsync"/>)。
    /// <para>任务级的 <see cref="WfTask.Version"/> 只能保证「同一件待办上只有一个动作胜出」,拦不住
    /// 「审批 vs 撤销」「超时 vs 人工」「终态写入 vs 重提」,以及并行网关下同实例两件待办各自 CAS 通过后
    /// 推进出互相冲突的实例终态。本列就是那一层的仲裁者。</para>
    /// <para><c>DefaultValue = "0"</c> 不是装饰,但它的作用机制和直觉不一样(2026-08-25 反编译
    /// <c>SqlSugarCore 5.1.4.198</c> 核实):<b>没有任何方言的 <c>AddColumnToTableSql</c> 模板含 DEFAULT
    /// 占位符</b>,所以它<b>不会</b>在 <c>ADD COLUMN</c> 里拼出 <c>DEFAULT 0</c> 子句。真正起作用的是
    /// <c>DbMaintenanceProvider.AddColumn</c> 的三步序列——当 <c>!IsNullable &amp;&amp;
    /// DefaultValue.HasValue()</c> 时:把 <c>IsNullable</c> 临时翻 <c>true</c> → 发一条**可空**的
    /// ADD COLUMN → <c>Updateable.AS(table).Where("&lt;col&gt; is null")</c> 回填 → 再 <c>UpdateColumn</c>
    /// 改成 NOT NULL。不写 <c>DefaultValue</c> 就走不到这条路,而 PostgreSQL / SQL Server 的
    /// <c>ADD COLUMN ... NOT NULL</c> 在表里已有行时会被直接拒绝(MySQL 才会隐式补 0)。
    /// <b>SQLite 例外</b>:<c>DefaultValue</c> 被 <c>ConnMoreSettings.SqliteCodeFirstEnableDefaultValue</c>
    /// 这个本仓未打开的开关吞掉(<c>SqlSugarSetup</c> 只设了 <c>SqlServerCodeFirstNvarchar</c>),故 SQLite
    /// 的 DDL 里不会出现 DEFAULT,但回填 UPDATE 照旧执行 → 旧行仍然读到 0。</para>
    /// </summary>
    [SugarColumn(ColumnDescription = "乐观锁版本", DefaultValue = "0")]
    public int Version { get; set; }

    /// <summary>发起摘要变量 JSON(金额/天数/类型…;够 branch 条件与列表摘要)。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "摘要变量 JSON")]
    public string? VariablesJson { get; set; }

    /// <summary>按节点 Id 保存发起人自选审批人,供后续请求恢复执行上下文。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "节点自选审批人 JSON")]
    public string? SelectedUserIdsJson { get; set; }

    /// <summary>
    /// 发起时按 <c>level</c> 快照的连续多级主管链(<see cref="ApproverProviderKeys.MultiLeader"/>);
    /// JSON 形如 <c>{"2":[...],"3":[...]}</c>;<c>null</c>=模型无 multiLeader 节点或老实例未快照。
    /// </summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "按级别多级主管链快照 JSON")]
    public string? LeaderChainJson { get; set; }
}
