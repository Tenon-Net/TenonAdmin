namespace TenonAdmin.Services;

/// <summary>个人资料出参(当前登录用户视角;不含密码哈希,§14)。</summary>
public record UserProfile
{
    public required long Id { get; init; }
    public required string Account { get; init; }
    public required string Name { get; init; }
    public long? OrgId { get; init; }
    public long? PositionId { get; init; }
    public required bool IsSuperAdmin { get; init; }
}

/// <summary>改个人资料入参:只允许改自己能改的字段(姓名);机构/职位/角色由管理员维护,不在此。</summary>
public record UpdateProfileInput
{
    public string Name { get; init; } = "";
}

/// <summary>改密码入参:须验旧密码(即便令牌未过期,也要证明你知道当前密码)。</summary>
public record ChangePasswordInput
{
    /// <summary>当前密码(校验用)</summary>
    public string OldPassword { get; init; } = "";

    /// <summary>新密码</summary>
    public string NewPassword { get; init; } = "";
}

/// <summary>设默认应用入参(多应用门户)。</summary>
public record SetDefaultModuleInput
{
    /// <summary>要设为默认的模块 Id(须为当前用户可访问的模块)。</summary>
    public long ModuleId { get; init; }
}
