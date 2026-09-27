using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 抄送已读表(<c>wf_cc</c>)——抄送不算待办,单独列表;进入节点时写入,用户已读后翻 <see cref="IsRead"/>。
/// </summary>
[SugarTable("wf_cc", TableDescription = "流程抄送")]
[SugarIndex("idx_wf_cc_instance", nameof(InstanceId), OrderByType.Asc)]
[SugarIndex("idx_wf_cc_user", nameof(UserId), OrderByType.Asc, nameof(IsRead), OrderByType.Asc)]
public class WfCc : BaseEntity
{
    [SugarColumn(ColumnDescription = "实例 Id")]
    public long InstanceId { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "节点 Id")]
    public string NodeId { get; set; } = "";

    [SugarColumn(ColumnDescription = "抄送接收人用户 Id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnDescription = "是否已读")]
    public bool IsRead { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "已读时间")]
    public DateTime? ReadTime { get; set; }

    /// <summary>
    /// <see cref="WfToken.NodeVisitId"/> 的拷贝(M3a-1);建行时从 <c>ctx.Token.NodeVisitId</c> 取。
    /// <b>不改</b> <c>(InstanceId, NodeId)</c> 去重键——重提重走同一 cc 节点时已有行的访问 Id 保持首次值,
    /// 不随重提刷新。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "节点访问 Id")]
    public long? NodeVisitId { get; set; }
}
