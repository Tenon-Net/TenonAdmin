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

    [SugarColumn(Length = 64, ColumnDescription = "昵称", IsNullable = true)]
    public string? Nickname { get; set; }

    [SugarColumn(Length = 32, ColumnDescription = "手机号", IsNullable = true)]
    public string? Phone { get; set; }

    [SugarColumn(Length = 128, ColumnDescription = "邮箱", IsNullable = true)]
    public string? Email { get; set; }

    /// <summary>性别;字典 gender 驱动,存字典项 Value("1"男/"2"女/"0"未知)。</summary>
    [SugarColumn(Length = 16, ColumnDescription = "性别", IsNullable = true)]
    public string? Gender { get; set; }

    /// <summary>
    /// 头像。存文件签名直链 ViewUrl(见 SysFileController.View)。
    /// ponytail: 存 ViewUrl 字符串直接进 &lt;img&gt;;签名密钥轮换后旧链失效、重传即修,够用。
    ///   要稳定改存 fileId(long)再按 id 换签名链。
    /// </summary>
    [SugarColumn(Length = 512, ColumnDescription = "头像", IsNullable = true)]
    public string? Avatar { get; set; }

    /// <summary>主属机构 Id(设计 §4"用户...主属机构");可空(超管/未分配)。数据范围以此为用户列表的机构维度(T3)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "主属机构 Id")]
    public long? OrgId { get; set; }

    /// <summary>职位 Id;可空(未分配)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "职位 Id")]
    public long? PositionId { get; set; }

    /// <summary>直属主管的用户 Id;可空(无上级/未分配)。软引用 sys_user,无导航属性。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "直属主管 Id")]
    public long? DirectorId { get; set; }

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

    /// <summary>
    /// 最后一次设置密码的时间(建号/管理员重置/自助改密均刷新)——密码过期判定的锚点(见 AuthService)。
    /// null = 过期功能上线前的存量用户:首次登录时回填为当时时间,过期窗口从那一刻起算,
    /// 避免功能启用当天全量老用户被一起判过期。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最后改密时间")]
    public DateTime? LastPasswordChangeTime { get; set; }
}
