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

    /// <summary>任务耗时(毫秒;从待办创建到本动作)。</summary>
    [SugarColumn(ColumnDescription = "耗时(毫秒)")]
    public long DurationMs { get; set; }

    /// <summary>转办目标用户 Id(仅 Transfer 时有值)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "转办目标用户 Id")]
    public long? TransferToUserId { get; set; }
}
