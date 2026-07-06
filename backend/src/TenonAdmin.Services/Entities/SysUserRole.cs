using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>用户-角色关联(设计 §16)。一个用户可挂多个角色;权限取并集。</summary>
[SugarTable("sys_user_role", TableDescription = "用户角色关联")]
[SugarIndex("idx_sys_user_role", nameof(UserId), OrderByType.Asc, nameof(RoleId), OrderByType.Asc, IsUnique = true)]
public class SysUserRole : BaseEntity
{
    [SugarColumn(ColumnDescription = "用户 Id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnDescription = "角色 Id")]
    public long RoleId { get; set; }
}
