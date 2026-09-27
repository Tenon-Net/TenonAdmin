using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>长期委托规则。规则按机构和原责任人占一个槽位，任务创建时只解析一次。</summary>
[SugarTable("wf_delegation_rule", TableDescription = "流程长期委托规则")]
[SugarIndex("uk_wf_delegation_scope_owner", nameof(ScopeOrgId), OrderByType.Asc, nameof(OriginalUserId), OrderByType.Asc, IsUnique = true)]
public class WfDelegationRule : BaseEntity
{
    [SugarColumn(ColumnDescription = "规则机构 Id")]
    public long ScopeOrgId { get; set; }

    [SugarColumn(ColumnDescription = "原责任人用户 Id")]
    public long OriginalUserId { get; set; }

    [SugarColumn(ColumnDescription = "委托目标用户 Id")]
    public long DelegateUserId { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    [SugarColumn(ColumnDescription = "生效时间(UTC)")]
    public DateTime StartsAt { get; set; }

    [SugarColumn(ColumnDescription = "失效时间(UTC,不含)")]
    public DateTime EndsAt { get; set; }

    [SugarColumn(ColumnDescription = "乐观锁版本")]
    public int Version { get; set; }
}

/// <summary>长期委托规则变更审计；规则软删后仍保留原快照。</summary>
[SugarTable("wf_delegation_rule_history", TableDescription = "流程长期委托规则审计")]
[SugarIndex("idx_wf_delegation_history_rule", nameof(RuleId), OrderByType.Asc, nameof(Id), OrderByType.Asc)]
public class WfDelegationRuleHistory : BaseEntity
{
    [SugarColumn(ColumnDescription = "规则 Id")]
    public long RuleId { get; set; }

    [SugarColumn(ColumnDescription = "规则机构 Id")]
    public long ScopeOrgId { get; set; }

    [SugarColumn(ColumnDescription = "原责任人用户 Id")]
    public long OriginalUserId { get; set; }

    [SugarColumn(ColumnDescription = "委托目标用户 Id")]
    public long DelegateUserId { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; }

    [SugarColumn(ColumnDescription = "生效时间(UTC)")]
    public DateTime StartsAt { get; set; }

    [SugarColumn(ColumnDescription = "失效时间(UTC,不含)")]
    public DateTime EndsAt { get; set; }

    [SugarColumn(ColumnDescription = "变更类型")]
    public WfDelegationRuleChangeType ChangeType { get; set; }

    [SugarColumn(ColumnDescription = "变更人用户 Id")]
    public long ActorUserId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "幂等请求键")]
    public string? RequestId { get; set; }
}
