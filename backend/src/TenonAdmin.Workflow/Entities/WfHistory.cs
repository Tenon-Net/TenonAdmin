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
}
