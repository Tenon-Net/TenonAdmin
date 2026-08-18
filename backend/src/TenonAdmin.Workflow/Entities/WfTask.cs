using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 活跃待办(<c>wf_task</c>)——完成即删并转入 <see cref="WfHisTask"/>。
/// 多副本下靠状态 CAS 防双批(引擎轮次落地)。
/// </summary>
[SugarTable("wf_task", TableDescription = "流程活跃待办")]
[SugarIndex("idx_wf_task_instance", nameof(InstanceId), OrderByType.Asc)]
[SugarIndex("idx_wf_task_token", nameof(TokenId), OrderByType.Asc)]
[SugarIndex("idx_wf_task_due", nameof(DueTime), OrderByType.Asc)]
public class WfTask : BaseEntity
{
    [SugarColumn(ColumnDescription = "实例 Id")]
    public long InstanceId { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "节点 Id")]
    public string NodeId { get; set; } = "";

    [SugarColumn(ColumnDescription = "token Id")]
    public long TokenId { get; set; }

    [SugarColumn(ColumnDescription = "签核模式(1 或签 / 2 会签 / 3 顺序)")]
    public WfSignMode SignMode { get; set; } = WfSignMode.Any;

    /// <summary>任务级乐观锁;每次办理/转办 CAS 递增,保证多副本只有一个动作胜出。</summary>
    [SugarColumn(ColumnDescription = "乐观锁版本")]
    public int Version { get; set; }

    /// <summary>到期时间(超时扫描用;空=不限)。M1 可空,超时 Job 属 M2。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "到期时间")]
    public DateTime? DueTime { get; set; }
}
