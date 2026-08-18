using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 机构表——组织架构树。前端拿平铺列表自行拼树(<c>ParentId</c> 挂靠),
/// 后端不维护物化树结构,避免"树 + 平铺"两份数据打架。
/// </summary>
[SugarTable("sys_org", TableDescription = "机构")]
[SugarIndex("idx_sys_org_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class SysOrg : BaseEntity
{
    /// <summary>父机构 Id(0=根,即顶级机构无父)</summary>
    [SugarColumn(ColumnDescription = "父机构 Id(0=根)")]
    public long ParentId { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "机构名称")]
    public string Name { get; set; } = "";

    /// <summary>机构编码(唯一,程序判机构用它而非名称)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "机构编码(唯一)")]
    public string Code { get; set; } = "";

    /// <summary>机构分类(存 org_category 字典 value,如 公司/部门/小组;可空)</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "机构分类")]
    public string? Category { get; set; }

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 机构负责人用户 Id;可空(未指定)。软引用 <c>sys_user</c>,无导航属性。
    /// 工作流「机构负责人」审批人 Provider 读此字段(<c>docs/review/workflow-design-plan-2026-08-17.md</c>)。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "机构负责人用户 Id")]
    public long? LeaderUserId { get; set; }
}
