using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程定义(<c>wf_definition</c>)——名称/图标/分组/状态/当前版本号;机构隔离(<see cref="DataEntity"/>)。
/// 版本快照见 <see cref="WfDefinitionVersion"/>;实例永远跑发布时的版本。
/// </summary>
[SugarTable("wf_definition", TableDescription = "流程定义")]
[SugarIndex("idx_wf_definition_group", nameof(GroupName), OrderByType.Asc)]
[SugarIndex("idx_wf_definition_status", nameof(Status), OrderByType.Asc)]
public class WfDefinition : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "流程名称")]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "图标")]
    public string? Icon { get; set; }

    /// <summary>分组名(列表筛选/设计器侧边分组;空=未分组)</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "分组")]
    public string? GroupName { get; set; }

    [SugarColumn(ColumnDescription = "状态(0 草稿 / 1 已发布 / 2 停用)")]
    public WfDefinitionStatus Status { get; set; } = WfDefinitionStatus.Draft;

    /// <summary>当前已发布版本号;0=从未发布。<see cref="WfDefinitionVersion.Version"/> 对齐。</summary>
    [SugarColumn(ColumnDescription = "当前版本号")]
    public int CurrentVersion { get; set; }
}
