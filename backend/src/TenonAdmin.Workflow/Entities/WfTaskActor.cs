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

    /// <summary>
    /// 进入 Pending、真正可被用户处理的时间(数据库评审 §4.3)。或签/会签/顺序首位在建任务时立刻写入
    /// (与 <c>CreateTime</c> 同一时刻);顺序会签的后位在被晋级为 Pending 那一刻写入,
    /// 之前的 Waiting 期间为空。<b>不新增 <c>AssignedTime</c> 列</b>——「成为候选办理人的时间」就是
    /// <c>CreateTime</c>,建两个语义相同的列是重复。
    /// <para>刻意 nullable 且不给 <c>DefaultValue</c>(升级策略同 <c>WfInstance.CompletedTime</c>):
    /// 升级前已是 Pending 的旧行永远读到 <c>null</c>,不做回填——它们的 <c>DurationMs</c> 计算会优雅退化回
    /// 旧公式(以 <c>WfTask.CreateTime</c> 兜底),不是缺陷。</para>
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "进入待处理时间")]
    public DateTime? ActivatedTime { get; set; }
}
