namespace TenonAdmin.Core;

/// <summary>
/// 密码复杂度策略(运行时可配置,见 <see cref="ISecurityPolicyProvider"/>)。
/// </summary>
/// <param name="MinLength">最小长度</param>
/// <param name="RequireUpper">须含大写字母</param>
/// <param name="RequireLower">须含小写字母</param>
/// <param name="RequireDigit">须含数字</param>
/// <param name="RequireSpecial">须含特殊字符(非字母数字)</param>
public record PasswordPolicy(int MinLength, bool RequireUpper, bool RequireLower, bool RequireDigit, bool RequireSpecial);

/// <summary>
/// 安全策略读取层(扩展点)。收口「DB(<c>SysConfig</c>)优先、Options 兜底」的解析:
/// 登录失败锁定阈值/时长、会话令牌时长、密码复杂度策略都从这里取——管理员在配置中心改动即时生效,
/// 不改代码不重发。默认实现放 Services 层,注册为 <c>TryAddScoped</c>,方法 <c>virtual</c> 可继承覆写。
/// </summary>
public interface ISecurityPolicyProvider
{
    /// <summary>登录失败锁定:最大失败次数 + 锁定分钟数(&lt;=0 的次数表示关闭锁定)。</summary>
    Task<(int MaxFailCount, int LockMinutes)> GetLoginLockAsync();

    /// <summary>会话令牌时长:访问令牌 + 刷新令牌的有效分钟数。</summary>
    Task<(int AccessMinutes, int RefreshMinutes)> GetSessionTtlAsync();

    /// <summary>密码复杂度策略。</summary>
    Task<PasswordPolicy> GetPasswordPolicyAsync();

    /// <summary>按当前策略校验口令;不满足抛 <see cref="ErrorCode.PasswordTooWeak"/>(args 携带策略明细供前端提示)。</summary>
    Task ValidatePasswordAsync(string password);

    /// <summary>
    /// 密码有效天数(&lt;=0 表示永不过期)。过期不拦登录,仅在登录时置 <c>MustChangePassword</c>
    /// 让前端强制跳转改密(与管理员重置密码同一信号)。
    /// 默认接口实现返回 0(关闭)——既有的自定义实现无需改动即可编译;内核默认实现从配置中心读取。
    /// </summary>
    Task<int> GetPasswordExpireDaysAsync() => Task.FromResult(0);

    /// <summary>
    /// 密码历史防重用条数(&lt;=0 表示关闭)。开启后改密/重置的新口令不得与当前或最近 N 个用过的口令相同。
    /// 默认接口实现返回 0(关闭)——既有的自定义实现无需改动即可编译;内核默认实现从配置中心读取。
    /// </summary>
    Task<int> GetPasswordHistoryCountAsync() => Task.FromResult(0);
}
