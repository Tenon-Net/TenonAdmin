using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>用户分页查询入参(账号/姓名模糊 + 机构/角色/启用状态过滤)</summary>
public record UserPageInput : PageInputBase
{
    /// <summary>账号(模糊)</summary>
    public string? Account { get; init; }

    /// <summary>姓名(模糊)</summary>
    public string? Name { get; init; }

    /// <summary>主属机构 Id(精确)</summary>
    public long? OrgId { get; init; }

    /// <summary>角色 Id(精确):只返回持有该角色的用户。管理端"这个角色有哪些人"的反查即走此参。</summary>
    public long? RoleId { get; init; }

    /// <summary>启用状态(null=全部)</summary>
    public bool? Enabled { get; init; }
}

/// <summary>
/// 用户列表/详情出参。<b>刻意不含 Password 字段</b>——密码哈希绝不出接口(设计 §14 敏感字段)。
/// </summary>
public record UserItem
{
    public required long Id { get; init; }
    public required string Account { get; init; }
    public required string Name { get; init; }
    public string? Nickname { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }

    /// <summary>性别字典值("1"男/"2"女/"0"未知);前端按字典 gender 翻译。</summary>
    public string? Gender { get; init; }

    /// <summary>头像签名直链。</summary>
    public string? Avatar { get; init; }

    public long? OrgId { get; init; }
    public long? PositionId { get; init; }
    public long? DirectorId { get; init; }

    /// <summary>机构名(不落 SysUser,分页时按 OrgId 关联 sys_org 补;仅列表展示用)</summary>
    public string? OrgName { get; init; }

    /// <summary>职位名(同上,按 PositionId 关联 sys_position 补)</summary>
    public string? PositionName { get; init; }

    /// <summary>直属主管姓名(同上,按 DirectorId 关联 sys_user 补)</summary>
    public string? DirectorName { get; init; }

    public required bool Enabled { get; init; }
    public required bool IsSuperAdmin { get; init; }

    /// <summary>管理员显式强制 TOTP(只能加严;超管/高敏持有者仍由策略自动强制)。</summary>
    public bool ForceTotp { get; init; }

    /// <summary>是否已绑定 TOTP(只读状态;绑定走 MFA 流程,不可经本字段写入)。</summary>
    public bool TotpEnabled { get; init; }

    public DateTime CreateTime { get; init; }
}

/// <summary>用户详情:列表字段 + 当前所属角色 Id 集合(编辑表单回显用)</summary>
public record UserDetail : UserItem
{
    public IReadOnlyCollection<long> RoleIds { get; init; } = [];
}

/// <summary>新增用户入参</summary>
public record AddUserInput
{
    /// <summary>登录账号(唯一,创建后不可改)</summary>
    public string Account { get; init; } = "";

    /// <summary>初始密码;留空则用默认初始密码(见 <c>UserService</c>)。IsSuperAdmin 不在入参——接口永不建超管(防提权)。</summary>
    public string? Password { get; init; }

    public string Name { get; init; } = "";
    public string? Nickname { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gender { get; init; }
    public string? Avatar { get; init; }
    public long? OrgId { get; init; }
    public long? PositionId { get; init; }
    public long? DirectorId { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>建号即显式强制 TOTP(用户须完成绑定后才能登录完成态)。</summary>
    public bool ForceTotp { get; init; }

    /// <summary>初始分配的角色 Id 集合</summary>
    public IReadOnlyCollection<long> RoleIds { get; init; } = [];
}

/// <summary>
/// 新增用户出参:新用户 Id + <b>实际生效的初始口令明文</b>。
/// <para>为什么要返回明文:管理员留空 <c>Password</c> 时,系统会按
/// <c>Security:DefaultInitialPassword</c>(默认未配)回落到密码学随机强口令——不回传就<b>谁也不知道这个号的密码</b>,
/// 建出来即死号,管理员只能再点一次"重置密码"才能拿到明文。与 <c>ResetPasswordAsync</c> 的出参语义一致:
/// 仅本次返回给管理员当场转达,不落库、不落日志(操作日志只记入参)。</para>
/// </summary>
public record AddUserOutput
{
    /// <summary>新用户 Id</summary>
    public required long Id { get; init; }

    /// <summary>实际生效的初始口令明文(管理员显式指定的、或系统生成的)</summary>
    public required string InitialPassword { get; init; }
}

/// <summary>重置密码入参。NewPassword 留空 = 重置为默认初始密码。</summary>
public record ResetPasswordInput
{
    public string? NewPassword { get; init; }
}

/// <summary>启停用入参</summary>
public record SetEnabledInput
{
    public bool Enabled { get; init; }
}

/// <summary>更新用户入参。不含 Account(不可改)、Password(走重置)、IsSuperAdmin(防提权)、TotpEnabled(只读)。</summary>
public record UpdateUserInput
{
    public string Name { get; init; } = "";
    public string? Nickname { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gender { get; init; }
    public string? Avatar { get; init; }
    public long? OrgId { get; init; }
    public long? PositionId { get; init; }
    public long? DirectorId { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>管理员显式强制 TOTP(只能加严;关断不解除超管/高敏自动强制)。</summary>
    public bool ForceTotp { get; init; }

    public IReadOnlyCollection<long> RoleIds { get; init; } = [];
}
