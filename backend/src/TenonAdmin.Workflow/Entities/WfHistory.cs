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

    /// <summary>事件载荷 JSON(任务 Id/动作/网关分支等;可空)。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "事件载荷 JSON")]
    public string? PayloadJson { get; set; }
}
