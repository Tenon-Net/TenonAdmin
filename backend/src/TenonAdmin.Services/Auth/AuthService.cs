using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IAuthService"/> 默认实现——模板方法样板(设计 §5.3):
/// <see cref="LoginAsync"/> 只编排流程,每一步都是 protected virtual 的小方法,
/// 用户继承本类覆写任意一步(如 <see cref="ValidateUserAsync"/> 换 LDAP 校验),
/// 前置 TryAdd 注册即接管,不必复制整个登录流程。
/// </summary>
public class AuthService(
    IRepository<SysUser> users,
    IPasswordHasher hasher,
    ITokenProvider tokens,
    ISessionService sessions,
    ILogService logService,
    ILoginLockService loginLock,
    ICaptchaService captcha) : IAuthService
{
    /// <summary>
    /// 防账号枚举的陪跑哈希:账号不存在时也执行一次真实代价的哈希校验,
    /// 使"账号不存在"与"密码错误"的响应耗时不可区分(否则攻击者可按耗时探测有效账号)。
    /// 进程内算一次缓存复用;并发首次的重复计算无害(结果相同,后写覆盖)。
    /// </summary>
    private static string? _dummyHash;

    /// <inheritdoc />
    public virtual async Task<LoginOutput> LoginAsync(LoginInput input)
    {
        try
        {
            await CheckLoginLockAsync(input);               // 0. 失败锁定检查(§14 防爆破,锁定期正确密码也拒)
            await ValidateCaptchaAsync(input);              // 1. 验证码(模块未接入时为直通)
            var user = await ValidateUserAsync(input);      // 2. 账密校验 —— 对接 LDAP/AD 覆写这步
            await CheckLoginPolicyAsync(user);              // 3. 策略检查(停用/锁定)
            var pair = await CreateTokenAsync(user);        // 4. 签发令牌
            await OnLoginSucceededAsync(user, pair);        // 5. 成功后置(登录日志/事件)
            return BuildLoginOutput(user, pair);            // 6. 组装出参
        }
        catch (AdminException ex)
        {
            // 任何业务失败(账密错/停用/验证码等)都记一条失败登录日志后原样抛出(§14 安全审计)
            await OnLoginFailedAsync(input, ex.Code);
            throw;
        }
    }

    /// <summary>失败锁定检查(§14):账号连续密码错误达阈值则在锁定窗口内拒绝(抛 <see cref="ErrorCode.AccountLocked"/>)。</summary>
    protected virtual Task CheckLoginLockAsync(LoginInput input) => loginLock.EnsureNotLockedAsync(input.Account);

    /// <summary>验证码校验(§14):启用时消费并校验票据(缺失/过期 40002、不匹配 40003);未启用直通。</summary>
    protected virtual Task ValidateCaptchaAsync(LoginInput input) => captcha.ValidateAsync(input.CaptchaId, input.CaptchaCode);

    /// <summary>
    /// 账密校验。安全细节(设计 §14):
    /// "账号不存在"与"密码错误"统一抛 <see cref="ErrorCode.PasswordWrong"/>(响应不可区分),
    /// 且账号不存在时也执行等价代价的哈希校验(耗时不可区分)——双通道一起堵死账号枚举。
    /// </summary>
    protected virtual async Task<SysUser> ValidateUserAsync(LoginInput input)
    {
        var user = await users.GetFirstAsync(u => u.Account == input.Account);
        if (user is null)
        {
            hasher.Verify(input.Password, _dummyHash ??= hasher.Hash("tenon-admin.timing-dummy"));
            throw new AdminException(ErrorCode.PasswordWrong);
        }

        if (!hasher.Verify(input.Password, user.Password))
            throw new AdminException(ErrorCode.PasswordWrong);

        return user;
    }

    /// <summary>登录策略检查:停用即拒;失败锁定(LoginLock)随安全模块接入时在此扩展。</summary>
    protected virtual Task CheckLoginPolicyAsync(SysUser user)
    {
        AdminException.ThrowIf(!user.Enabled, ErrorCode.AccountDisabled);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 签发令牌 + 开会话(设计 §15)。SessionId 用 GUID v7(时间有序,BCL 内置)——在线用户与强退的稳定锚点;
    /// <see cref="ISessionService.OpenAsync"/> 负责落库/缓存会话、存刷新令牌哈希、执行单端/限并发策略。
    /// </summary>
    protected virtual async Task<TokenPair> CreateTokenAsync(SysUser user)
    {
        var sessionId = Guid.CreateVersion7().ToString("N");
        var pair = tokens.Create(new TokenSubject(user.Id, user.Account, sessionId, user.IsSuperAdmin));
        await sessions.OpenAsync(user, sessionId, pair);
        return pair;
    }

    /// <inheritdoc />
    public virtual async Task<LoginOutput> RefreshAsync(RefreshInput input)
    {
        var refreshed = await sessions.RefreshAsync(input.RefreshToken);
        return BuildLoginOutput(refreshed.User, refreshed.Pair);
    }

    /// <inheritdoc />
    public virtual Task LogoutAsync(string sessionId) => sessions.RevokeAsync(sessionId);

    /// <summary>
    /// 登录成功后置钩子:写成功登录日志(§4/§14)。也是用户挂自定义动作(发登录事件、更新最后登录时间等)的扩展点——
    /// 覆写时记得 <c>base.OnLoginSucceededAsync(...)</c> 保留日志,或自行接管。
    /// </summary>
    protected virtual async Task OnLoginSucceededAsync(SysUser user, TokenPair pair)
    {
        await loginLock.ResetAsync(user.Account);   // 成功即清零失败计数
        await logService.RecordLoginAsync(new LoginLogEntry { Account = user.Account, Success = true, ResultCode = 0, UserId = user.Id });
    }

    /// <summary>
    /// 登录失败后置钩子:写失败登录日志(§14)。记<b>原始输入账号</b>(哪怕账号不存在)+ 具体失败码,
    /// 供暴力破解/账号探测排查;IP/UA 由日志服务从当前请求补全。绝不记密码。
    /// <para>仅"密码错误"计入失败锁定——验证码错/已锁定/停用等不累加,避免把锁定窗口无限延长或误伤。</para>
    /// </summary>
    protected virtual async Task OnLoginFailedAsync(LoginInput input, ErrorCode code)
    {
        if (code == ErrorCode.PasswordWrong)
            await loginLock.RecordFailureAsync(input.Account);
        await logService.RecordLoginAsync(new LoginLogEntry { Account = input.Account, Success = false, ResultCode = (int)code });
    }

    /// <summary>组装登录出参(要给前端加返回字段,覆写这步)。</summary>
    protected virtual LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) => new()
    {
        AccessToken = pair.AccessToken,
        ExpiresAt = pair.ExpiresAt,
        RefreshToken = pair.RefreshToken,
        RefreshExpiresAt = pair.RefreshExpiresAt,
        UserId = user.Id,
        Account = user.Account,
        Name = user.Name,
    };
}
