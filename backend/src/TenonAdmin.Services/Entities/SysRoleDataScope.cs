using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 角色数据范围(设计 §16,T3)——每角色一条,描述该角色可见数据的机构维度(<see cref="DataScopeType"/>)。
/// 用户的生效范围 = 其所有角色范围的合并(见 <c>DataScopeProvider</c>)。
/// </summary>
[SugarTable("sys_role_data_scope", TableDescription = "角色数据范围")]
[SugarIndex("idx_sys_role_data_scope_role", nameof(RoleId), OrderByType.Asc, IsUnique = true)]
public class SysRoleDataScope : BaseEntity
{
    [SugarColumn(ColumnDescription = "角色 Id(唯一)")]
    public long RoleId { get; set; }

    [SugarColumn(ColumnDescription = "范围类型")]
    public DataScopeType ScopeType { get; set; }

    /// <summary>自定义机构 Id 列表(逗号分隔),仅 <see cref="DataScopeType.Custom"/> 时使用。</summary>
    [SugarColumn(Length = 512, ColumnDescription = "自定义机构 Id(逗号分隔)")]
    public string CustomOrgIds { get; set; } = "";
}
