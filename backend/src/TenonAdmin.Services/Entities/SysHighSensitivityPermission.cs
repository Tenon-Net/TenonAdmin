using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 高敏感权限码的消费者自定义追加项(等保三级应用安全一期)。
/// 内核默认高敏集合硬编码在服务内、不可经本表删除;本表仅存消费者追加的权限码。
/// </summary>
[SugarTable("sys_high_sensitivity_permission", TableDescription = "高敏感权限(自定义追加)")]
[SugarIndex("idx_sys_hs_perm_code", nameof(PermissionCode), OrderByType.Asc, IsUnique = true)]
public class SysHighSensitivityPermission : BaseEntity
{
    /// <summary>权限码(路由权限码,如 <c>POST:/api/v1/sys/user</c>)</summary>
    [SugarColumn(Length = 256, ColumnDescription = "权限码")]
    public string PermissionCode { get; set; } = "";

    /// <summary>备注(可选,说明为何追加为高敏)</summary>
    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
