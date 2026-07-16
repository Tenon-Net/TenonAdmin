namespace TenonAdmin.Services;

/// <summary>个人资料出参(当前登录用户视角;不含密码哈希,§14)。</summary>
public record UserProfile
{
    public required long Id { get; init; }
    public required string Account { get; init; }
    public required string Name { get; init; }
    public long? OrgId { get; init; }
    public long? PositionId { get; init; }

    /// <summary>机构名称(已删/未分配则为 null)。前端只有 Id 的话就只能把雪花号原样甩给用户看。</summary>
    public string? OrgName { get; init; }

    /// <summary>职位名称(已删/未分配则为 null)</summary>
    public string? PositionName { get; init; }

    public string? Nickname { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }

    /// <summary>性别:字典 gender 的项 Value("1"男/"2"女/"0"未知),同 <see cref="SysUser.Gender"/>。</summary>
    public string? Gender { get; init; }

    /// <summary>头像(文件签名直链 ViewUrl,直接进 img)。</summary>
    public string? Avatar { get; init; }

    public required bool IsSuperAdmin { get; init; }
}

/// <summary>
/// 改个人资料入参:只允许改自己能改的字段(姓名/昵称/性别/手机/邮箱/头像);机构/职位/角色由管理员维护,不在此。
/// 全量替换语义(与管理端 UpdateUserInput 一致):前端须整表单提交,缺字段即置空。
/// 手机/邮箱不做二次验证——内核无短信/邮件通道,管理员本可在用户管理任改这两个字段,操作日志留痕。
/// </summary>
public record UpdateProfileInput
{
    public string Name { get; init; } = "";
    public string? Nickname { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gender { get; init; }
    public string? Avatar { get; init; }
}

/// <summary>"我的会话"列表项(个人视角;不含 UserId/Account——都是自己的)。</summary>
public record MySessionItem
{
    public required string SessionId { get; init; }
    public string? Ip { get; init; }

    /// <summary>登录时的 User-Agent 原串(前端解析成浏览器/系统展示,分辨多端)</summary>
    public string? UserAgent { get; init; }

    public DateTime LoginTime { get; init; }
    public DateTime ExpiresAt { get; init; }

    /// <summary>是否本次请求所用会话(踢当前会话等于自杀,前端据此置灰,让用户走退出登录)。</summary>
    public required bool IsCurrent { get; init; }
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
