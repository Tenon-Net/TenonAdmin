namespace TenonAdmin.Core;

/// <summary>
/// 当前登录用户上下文(扩展点)——从令牌 claim 读取,不查库。
/// <para>数据范围解析、审计字段填充(T4)、操作日志(T6)都从这里取"当前是谁"。
/// 默认实现走 <c>IHttpContextAccessor</c> 读 JWT claim(sub/sadm)。</para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>是否已认证(有有效令牌)</summary>
    bool IsAuthenticated { get; }

    /// <summary>当前用户 Id;未认证为 null</summary>
    long? UserId { get; }

    /// <summary>是否超级管理员(令牌 sadm claim);超管不受权限与数据范围约束</summary>
    bool IsSuperAdmin { get; }
}
