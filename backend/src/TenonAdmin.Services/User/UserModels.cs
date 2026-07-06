using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>用户分页查询入参(账号/姓名模糊 + 机构/启用状态过滤)</summary>
public record UserPageInput : PageInputBase
{
    /// <summary>账号(模糊)</summary>
    public string? Account { get; init; }

    /// <summary>姓名(模糊)</summary>
    public string? Name { get; init; }

    /// <summary>主属机构 Id(精确)</summary>
    public long? OrgId { get; init; }

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
    public long? OrgId { get; init; }
    public long? PositionId { get; init; }
    public required bool Enabled { get; init; }
    public required bool IsSuperAdmin { get; init; }
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
    public long? OrgId { get; init; }
    public long? PositionId { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>初始分配的角色 Id 集合</summary>
    public IReadOnlyCollection<long> RoleIds { get; init; } = [];
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

/// <summary>更新用户入参。不含 Account(不可改)、Password(走重置)、IsSuperAdmin(防提权)。</summary>
public record UpdateUserInput
{
    public string Name { get; init; } = "";
    public long? OrgId { get; init; }
    public long? PositionId { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlyCollection<long> RoleIds { get; init; } = [];
}
