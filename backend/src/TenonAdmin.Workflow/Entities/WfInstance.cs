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

    /// <summary>发起摘要变量 JSON(金额/天数/类型…;够 branch 条件与列表摘要)。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "摘要变量 JSON")]
    public string? VariablesJson { get; set; }

    /// <summary>按节点 Id 保存发起人自选审批人,供后续请求恢复执行上下文。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "节点自选审批人 JSON")]
    public string? SelectedUserIdsJson { get; set; }
}
