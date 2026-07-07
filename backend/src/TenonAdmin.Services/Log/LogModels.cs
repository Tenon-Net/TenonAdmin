using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 操作日志录入条目——过滤器把它<b>已知的 HTTP 侧字段</b>交给 <see cref="ILogService"/>,
/// 操作人/IP/UA 由服务从当前登录态补全(过滤器保持轻薄)。
/// </summary>
public record OperationLogEntry
{
    /// <summary>操作名(来自 <c>[OperationLog]</c> 标题)</summary>
    public string Title { get; init; } = "";
    public string HttpMethod { get; init; } = "";
    public string Path { get; init; } = "";
    /// <summary>脱敏后的入参 JSON</summary>
    public string? ParamJson { get; init; }
    public int ResultCode { get; init; }
    public long ElapsedMs { get; init; }
}

/// <summary>登录日志录入条目——由 <c>AuthService</c> 在成功/失败路径提交;IP/UA 由服务补全。</summary>
public record LoginLogEntry
{
    public string Account { get; init; } = "";
    public bool Success { get; init; }
    /// <summary>结果码(0 成功;失败为具体原因码)</summary>
    public int ResultCode { get; init; }
    /// <summary>登录成功时的用户 Id</summary>
    public long? UserId { get; init; }
}

/// <summary>操作日志分页查询入参:按操作名模糊 + 成败精确过滤。</summary>
public record OpLogPageInput : PageInputBase
{
    /// <summary>操作名(模糊匹配,可选)</summary>
    public string? Title { get; init; }

    /// <summary>是否成功(可选,不传则不限)</summary>
    public bool? Success { get; init; }
}

/// <summary>登录日志分页查询入参:按账号模糊 + 成败精确过滤。</summary>
public record LoginLogPageInput : PageInputBase
{
    /// <summary>登录账号(模糊匹配,可选)</summary>
    public string? Account { get; init; }

    /// <summary>是否成功(可选,不传则不限)</summary>
    public bool? Success { get; init; }
}
