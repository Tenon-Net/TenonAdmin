using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 任务办理人(<c>wf_task_actor</c>,1:N)——"我的待办"查本表 Pending + Approver。
/// </summary>
[SugarTable("wf_task_actor", TableDescription = "流程任务办理人")]
[SugarIndex("idx_wf_task_actor_task", nameof(TaskId), OrderByType.Asc)]
[SugarIndex("idx_wf_task_actor_user", nameof(UserId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public class WfTaskActor : BaseEntity
{
    [SugarColumn(ColumnDescription = "待办任务 Id")]
    public long TaskId { get; set; }

    [SugarColumn(ColumnDescription = "办理人用户 Id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnDescription = "类型(1 审批 / 2 抄送)")]
    public WfActorType ActorType { get; set; } = WfActorType.Approver;

    [SugarColumn(ColumnDescription = "状态(1 待处理 / 2 已处理 / 3 跳过)")]
    public WfActorStatus Status { get; set; } = WfActorStatus.Pending;

    /// <summary>顺序会签时的次序(从 1 起;或签/会签可 0)。</summary>
    [SugarColumn(ColumnDescription = "办理顺序")]
    public int Sort { get; set; }
}
