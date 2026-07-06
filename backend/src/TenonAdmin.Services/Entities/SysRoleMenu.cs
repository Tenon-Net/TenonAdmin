using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>角色-菜单关联(设计 §16)。角色授权即维护本表;用户权限码由此聚合。</summary>
[SugarTable("sys_role_menu", TableDescription = "角色菜单关联")]
[SugarIndex("idx_sys_role_menu", nameof(RoleId), OrderByType.Asc, nameof(MenuId), OrderByType.Asc, IsUnique = true)]
public class SysRoleMenu : BaseEntity
{
    [SugarColumn(ColumnDescription = "角色 Id")]
    public long RoleId { get; set; }

    [SugarColumn(ColumnDescription = "菜单 Id")]
    public long MenuId { get; set; }
}
