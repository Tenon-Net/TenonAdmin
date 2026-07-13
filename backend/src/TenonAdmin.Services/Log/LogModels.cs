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
    /// <summary>动作抛异常时的异常消息(截断);无异常为 null。不含响应体。</summary>
    public string? ExceptionMessage { get; init; }
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

/// <summary>
/// 操作日志分页查询入参。审计日志要回答的是"<b>谁</b>在<b>什么时候</b>干了<b>什么</b>",
/// 故除操作名/成败外,还必须能按操作人、时间范围、接口路径筛——否则"上周三谁删了那批数据"只能靠一页页翻。
/// </summary>
public record OpLogPageInput : PageInputBase
{
    /// <summary>操作名(模糊匹配,可选)</summary>
    public string? Title { get; init; }

    /// <summary>是否成功(可选,不传则不限)</summary>
    public bool? Success { get; init; }

    /// <summary>操作人用户 Id(精确;日志只存 Id,姓名是读取时回填的,故不做姓名模糊)</summary>
    public long? OperatorId { get; init; }

    /// <summary>接口路径(模糊,可选;如 <c>/api/v1/sys/role</c> 可捞出角色相关的全部动作)</summary>
    public string? Path { get; init; }

    /// <summary>操作时间下界(含)</summary>
    public DateTime? StartTime { get; init; }

    /// <summary>操作时间上界(含)</summary>
    public DateTime? EndTime { get; init; }
}

/// <summary>登录日志分页查询入参:按账号模糊 + 成败精确 + 时间范围过滤。</summary>
public record LoginLogPageInput : PageInputBase
{
    /// <summary>登录账号(模糊匹配,可选)</summary>
    public string? Account { get; init; }

    /// <summary>是否成功(可选,不传则不限)</summary>
    public bool? Success { get; init; }

    /// <summary>登录时间下界(含)</summary>
    public DateTime? StartTime { get; init; }

    /// <summary>登录时间上界(含)</summary>
    public DateTime? EndTime { get; init; }
}
