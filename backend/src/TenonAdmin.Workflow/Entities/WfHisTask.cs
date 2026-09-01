using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 历史任务(<c>wf_his_task</c>)——审批记录页数据源;活跃待办完成后转入本表。
/// </summary>
[SugarTable("wf_his_task", TableDescription = "流程历史任务")]
[SugarIndex("idx_wf_his_task_instance", nameof(InstanceId), OrderByType.Asc)]
[SugarIndex("idx_wf_his_task_user", nameof(UserId), OrderByType.Asc)]
public class WfHisTask : BaseEntity
{
    [SugarColumn(ColumnDescription = "实例 Id")]
    public long InstanceId { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "节点 Id")]
    public string NodeId { get; set; } = "";

    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "节点名称(冗余展示)")]
    public string? NodeName { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "原待办任务 Id")]
    public long? TaskId { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "token Id")]
    public long? TokenId { get; set; }

    [SugarColumn(ColumnDescription = "办理人用户 Id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnDescription = "动作(1 同意 / 2 拒绝 / 3 转办 / …)")]
    public WfTaskAction Action { get; set; }

    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "审批意见")]
    public string? Comment { get; set; }

    /// <summary>
    /// 任务耗时(毫秒)。语义:<see cref="StartedTime"/> 有值时是「真正可处理到本动作」的耗时,为空时
    /// 优雅退化为「待办创建到本动作」(数据库评审 §4.3)。
    /// </summary>
    [SugarColumn(ColumnDescription = "耗时(毫秒)")]
    public long DurationMs { get; set; }

    /// <summary>
    /// 本动作对应的 <c>wf_task_actor.ActivatedTime</c> 快照(数据库评审 §4.3)。转办/顺序会签下,继承
    /// 前手等待时间或跳过等待轮到自己的时间都不该计入本次办理人的真实耗时——<see cref="DurationMs"/>
    /// 因此改以本字段为基准,不再用 <see cref="WfTask"/>.<c>CreateTime</c>(那是整件任务的创建时间,不是本办理
    /// 人的接手时间)。升级前的旧行与本办理人从未进入 Pending 前直接办理的边界情形均可能为空,退化到旧公式。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "本次办理起算时间")]
    public DateTime? StartedTime { get; set; }

    /// <summary>转办 / 委托目标用户 Id(仅 Transfer 与 Delegate 时有值)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "转办目标用户 Id")]
    public long? TransferToUserId { get; set; }

    /// <summary>
    /// <see cref="WfToken.NodeVisitId"/> 的拷贝(M3a-1);从 <c>Task.NodeVisitId</c>(活跃待办行)取,
    /// 而非 <c>ctx.Token.NodeVisitId</c>——读任务行才准确表达「这件待办是哪一次访问建的」。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "节点访问 Id")]
    public long? NodeVisitId { get; set; }
}
