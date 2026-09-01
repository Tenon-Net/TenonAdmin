using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// append-only 事件流(<c>wf_history</c>)——实例创建/节点进出/网关/超时等;审计与排查用投影,
/// 不做 Temporal 式确定性重放(崩溃恢复看运行表状态)。
/// </summary>
[SugarTable("wf_history", TableDescription = "流程事件流")]
[SugarIndex("idx_wf_history_instance", nameof(InstanceId), OrderByType.Asc, nameof(CreateTime), OrderByType.Asc)]
[SugarIndex("idx_wf_history_event", nameof(EventType), OrderByType.Asc)]
public class WfHistory : BaseEntity
{
    [SugarColumn(ColumnDescription = "实例 Id")]
    public long InstanceId { get; set; }

    [SugarColumn(ColumnDescription = "事件类型")]
    public WfHistoryEventType EventType { get; set; }

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "相关节点 Id")]
    public string? NodeId { get; set; }

    /// <summary>
    /// 产生这条事件的那次用户动作的幂等请求键(<c>RequestId</c>);<c>null</c> = 本次没有请求身份。
    /// <para>与 <c>wf_operation_receipt.RequestKey</c> **同源不同名**(两张表各自的既有命名,不强行统一):
    /// 值来自 <see cref="WfWriteCmd.RequestId"/>,归一化全仓只有那一份。排障时靠它把「一次点击」
    /// 与它引发的整串事件串起来。</para>
    /// <para><b>写 <c>null</c> 是语义而非遗漏</b>:超时(<see cref="WfTimeoutJob"/>)与催办
    /// (<c>UrgeAsync</c>)都绕开执行上下文直插本表 —— 前者是系统扫出来的、没有"用户这一次点击",
    /// 后者刻意不做幂等。**不要**给它们补上这个值。</para>
    /// <para>可空、无默认值、不建索引:与 <see cref="WfInstance.CompletedTime"/> 同型,存量表
    /// <c>ADD COLUMN</c> 四库都接受,不触发"先加可空列 → 回填 → 改 NOT NULL"三步路。旧行读到 <c>null</c>。</para>
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "幂等请求键(无请求身份为空)")]
    public string? RequestId { get; set; }

    /// <summary>事件载荷 JSON(任务 Id/动作/网关分支等;可空)。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "事件载荷 JSON")]
    public string? PayloadJson { get; set; }

    /// <summary>
    /// 产生本事件时的活跃 token Id(M3a-1)。<see cref="WfExecutionContext.AppendHistoryAsync"/> 从
    /// <c>ctx.Token.Id</c> 取;绕开 ctx 的 4 个系统写入点(<see cref="WfTimeoutJob"/> ×3、
    /// <c>WfTaskService.UrgeAsync</c> ×1)从 <c>task.TokenId</c> 取。<b>不存在写不出的情况</b>:token 行
    /// 从不物理删(只翻 <see cref="WfToken.Status"/>),<c>InstanceStarted</c> 那行也有值。保持可空只为
    /// 旧行与将来真正的实例级(无 token)事件。可空、无默认值,与 <see cref="RequestId"/> 同型三步升级豁免。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "产生本事件的 token Id")]
    public long? TokenId { get; set; }

    /// <summary><see cref="WfToken.NodeVisitId"/> 的拷贝(M3a-1),来源与 <see cref="TokenId"/> 同款规则。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "节点访问 Id")]
    public long? NodeVisitId { get; set; }

    /// <summary>
    /// 本实例内的严格递增序号(M3a-1),从 1 起、无重复、无间隙(事务回滚连计数器递增一起回滚)。由
    /// <see cref="WfHistorySequence.NextAsync"/> 在写行前于同事务内原子分配——<c>wf_instance.HistorySeq</c>
    /// 相对递增 + 读回,四库通用,不靠 <c>MAX(Sequence)+1</c>(必撞号)或读-then-CAS(MySQL RR 下活锁)。
    /// <para>非空带默认列:<c>DefaultValue="0"</c> 触发 SqlSugar 的「先加可空列 → 回填 → 改 NOT NULL」三步
    /// 升级序列,升级前的存量行读到 0(与真实序号从 1 起不冲突,足以区分「升级前」与「升级后」)。</para>
    /// <para><b>本轮刻意不建 <c>UNIQUE(InstanceId, Sequence)</c></b>:原子递增本身已保证不重复,唯一索引
    /// 只在「递增逻辑写错」时才会拦下——那种情况下测试与代码评审是更早的防线,四库上再加一条复合唯一索引
    /// 换不来相称的收益,且徒增建表/迁移面。</para>
    /// </summary>
    [SugarColumn(ColumnDescription = "实例内严格递增序号", DefaultValue = "0")]
    public int Sequence { get; set; }

    /// <summary>
    /// 触发本事件的行为者类型(M3a-1)。<c>DefaultValue="0"</c>(<see cref="WfHistoryActorType.Unknown"/>)
    /// 同 <see cref="Sequence"/> 走三步升级序列,升级前的旧行读到 <see cref="WfHistoryActorType.Unknown"/>。
    /// </summary>
    [SugarColumn(ColumnDescription = "行为者类型", DefaultValue = "0")]
    public WfHistoryActorType ActorType { get; set; }

    /// <summary>触发本事件的用户 Id;系统/超时等无用户身份的事件为 <c>null</c>。可空、无默认值。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "行为者用户 Id")]
    public long? ActorUserId { get; set; }

    /// <summary>
    /// <see cref="PayloadJson"/> 的形状版本(M3a-1)。读取方按 <c>EventType + PayloadVersion</c> 解释载荷;
    /// 没人显式写——C# 默认值 1 覆盖新行,<c>DefaultValue="1"</c> 覆盖旧行,只有某个
    /// <see cref="WfHistoryEventType"/> 的 payload 形状将来变了,才在那一个写入点显式抬到 2。
    /// Task 1 不动任何值。非空带默认列,同 <see cref="Sequence"/> 走三步升级序列。
    /// </summary>
    [SugarColumn(ColumnDescription = "载荷形状版本", DefaultValue = "1")]
    public int PayloadVersion { get; set; } = 1;
}
