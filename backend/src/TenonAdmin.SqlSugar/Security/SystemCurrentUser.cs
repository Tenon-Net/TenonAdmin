using TenonAdmin.Core;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// <see cref="ICurrentUser"/> 的兜底实现:恒"未认证/系统上下文"。
/// HTTP 环境由 AspNetCore 层的 <c>HttpContextCurrentUser</c> 前置注册覆盖;
/// 非 HTTP(启动、种子、自检、后台)用它——审计字段填充遇它则留空(系统写入不归属某用户)。
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public long? UserId => null;
    public bool IsSuperAdmin => false;

    // 非 HTTP 上下文(启动/种子/后台)没有请求来源,IP/UA 恒 null
    public string? IpAddress => null;
    public string? UserAgent => null;
}
