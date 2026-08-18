using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程定义版本快照(<c>wf_definition_version</c>)——发布即不可变;实例挂本表 Id 跑固定 schema。
/// <para><see cref="ModelJson"/> 存钉钉树 schema;<see cref="FormSchema"/> M1 预留空值,避免 M3 加列迁移。</para>
/// </summary>
[SugarTable("wf_definition_version", TableDescription = "流程定义版本快照")]
[SugarIndex("uk_wf_definition_version", nameof(DefinitionId), OrderByType.Asc, nameof(Version), OrderByType.Asc, IsUnique = true)]
public class WfDefinitionVersion : BaseEntity
{
    [SugarColumn(ColumnDescription = "流程定义 Id")]
    public long DefinitionId { get; set; }

    /// <summary>从 1 起递增;与 <see cref="WfDefinition.CurrentVersion"/> 对齐的是最近一次成功发布。</summary>
    [SugarColumn(ColumnDescription = "版本号")]
    public int Version { get; set; }

    /// <summary>钉钉树 JSON schema(含节点 formPerms 等;权威在后端模型)。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, ColumnDescription = "流程模型 JSON")]
    public string ModelJson { get; set; } = "";

    /// <summary>M3 简易动态表单控件描述;M1/M2 恒空。与 schema 内 formSchema 同步预留,避免加列。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "表单 schema(M1 预留)")]
    public string? FormSchema { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "发布时间")]
    public DateTime? PublishTime { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "发布人用户 Id")]
    public long? PublishUserId { get; set; }
}
