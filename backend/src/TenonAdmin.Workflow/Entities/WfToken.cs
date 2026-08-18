using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 运行 token(<c>wf_token</c>)——实例在图上的执行指针。一期串行≈每实例 1 活跃 token;
/// 并行网关(M3)启用后多 token,<b>表结构先到位</b>。
/// </summary>
[SugarTable("wf_token", TableDescription = "流程运行 token")]
[SugarIndex("idx_wf_token_instance", nameof(InstanceId), OrderByType.Asc)]
[SugarIndex("idx_wf_token_instance_status", nameof(InstanceId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public class WfToken : BaseEntity
{
    [SugarColumn(ColumnDescription = "实例 Id")]
    public long InstanceId { get; set; }

    /// <summary>当前所在节点 Id(schema 内节点 id,非雪花)。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "节点 Id")]
    public string NodeId { get; set; } = "";

    [SugarColumn(ColumnDescription = "状态(1 活跃 / 2 完成 / 3 取消)")]
    public WfTokenStatus Status { get; set; } = WfTokenStatus.Active;
}
