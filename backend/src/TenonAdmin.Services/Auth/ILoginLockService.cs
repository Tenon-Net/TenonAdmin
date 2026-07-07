using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 登录失败锁定(设计 §14)——按账号统计连续密码错误次数,达阈值即在锁定窗口内拒绝登录,防暴力破解。
/// 计数进 <see cref="ICacheProvider"/>,窗口靠缓存 TTL 滑动过期(停手即自动解锁)。
/// </summary>
public interface ILoginLockService
{
    /// <summary>若账号已被锁定则抛 <see cref="ErrorCode.AccountLocked"/>(args 带 lockMinutes);未开启或未锁定则放行。</summary>
    Task EnsureNotLockedAsync(string account);

    /// <summary>记一次登录失败(计数 +1,并刷新锁定窗口 TTL)。仅密码错误应计入。</summary>
    Task RecordFailureAsync(string account);

    /// <summary>清除账号的失败计数(登录成功时调用)。</summary>
    Task ResetAsync(string account);
}
