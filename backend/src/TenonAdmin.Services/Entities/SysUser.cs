using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 用户表(设计 §16)。认证闭环最小字段集;
/// 组织/职位/头像等字段随组织模块(M1 后续纵切)增补——CodeFirst 会自动补列,先窄后宽零成本。
/// </summary>
[SugarTable("sys_user", TableDescription = "用户")]
[SugarIndex("idx_sys_user_account", nameof(Account), OrderByType.Asc, IsUnique = true)]
public class SysUser : BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "登录账号(唯一)")]
    public string Account { get; set; } = "";

    /// <summary>密码哈希——自描述格式(算法.迭代.盐.哈希),绝不存明文;格式见 Pbkdf2PasswordHasher</summary>
    [SugarColumn(Length = 256, ColumnDescription = "密码哈希")]
    public string Password { get; set; } = "";

    [SugarColumn(Length = 64, ColumnDescription = "姓名/昵称")]
    public string Name { get; set; } = "";

    /// <summary>主属机构 Id(设计 §4"用户...主属机构");可空(超管/未分配)。数据范围以此为用户列表的机构维度(T3)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "主属机构 Id")]
    public long? OrgId { get; set; }

    /// <summary>职位 Id;可空(未分配)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "职位 Id")]
    public long? PositionId { get; set; }

    /// <summary>停用后无法登录、已有会话由权限过滤器拦截(设计 §15)</summary>
    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 超级管理员标志:授权管道直接放行、不受 RBAC 约束(设计 §6 授权管道第一步)。
    /// 只能由种子/数据库手工设置,接口永远不暴露修改入口——防提权。
    /// </summary>
    [SugarColumn(ColumnDescription = "是否超级管理员")]
    public bool IsSuperAdmin { get; set; }

    /// <summary>默认应用/模块 Id(多应用门户):登录后默认进入的应用;可空(未设=让用户选)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "默认模块 Id")]
    public long? DefaultModuleId { get; set; }

    /// <summary>
    /// 是否需在下次登录后强制修改密码(设计 §14)。管理员建号/重置密码时置 true,
    /// 用户自助改密成功后清 false。不拦登录,仅经登录出参透传给前端做强制跳转。
    /// </summary>
    [SugarColumn(ColumnDescription = "是否需强制改密")]
    public bool MustChangePassword { get; set; }
}
