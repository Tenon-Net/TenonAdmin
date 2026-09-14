using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>一次并行 fork 的臂状态。</summary>
[SugarTable("wf_parallel_arm", TableDescription = "流程并行臂")]
[SugarIndex("idx_wf_parallel_arm_parent_status", nameof(ParentTokenId), OrderByType.Asc,
    nameof(Status), OrderByType.Asc)]
public class WfParallelArm
{
    [SugarColumn(IsPrimaryKey = true, ColumnDescription = "并行 fork Id")]
    public long ForkId { get; set; }

    [SugarColumn(IsPrimaryKey = true, Length = 64, ColumnDescription = "并行臂 Id")]
    public string ArmId { get; set; } = "";

    [SugarColumn(ColumnDescription = "父 token Id")]
    public long ParentTokenId { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "子 token Id")]
    public long? ChildTokenId { get; set; }

    [SugarColumn(ColumnDescription = "状态(1 活跃 / 2 完成 / 3 取消)")]
    public WfParallelArmStatus Status { get; set; } = WfParallelArmStatus.Active;

    [SugarColumn(ColumnDescription = "乐观锁版本", DefaultValue = "0")]
    public int Version { get; set; }

    [SugarColumn(ColumnDescription = "父节点访问 Id")]
    public long ParentNodeVisitId { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "子臂入口节点访问 Id")]
    public long? ChildEntryNodeVisitId { get; set; }

    [SugarColumn(IsNullable = true, Length = 512, ColumnDescription = "完成或取消原因")]
    public string? Reason { get; set; }
}
