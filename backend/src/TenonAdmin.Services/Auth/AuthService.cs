using System.Security.Cryptography;
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
    ICaptchaService captcha,
    ISecurityPolicyProvider policy,
    ISmsOtpService smsOtp,
    // 外部登录 / SSO(批次 D):可选参数,DI 正常注入;消费者子类省略也能编译(§5.3,同 UserService 的 passwordHistory)
    IEnumerable<IExternalAuthProvider>? externalProviders = null,
    ISysUserExternalService? externalBindings = null,
    IRbacService? rbac = null) : IAuthService
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
            await CheckPasswordExpiryAsync(user);           // 4. 密码过期检查(过期→置强制改密标志,不拦登录)
            await CheckSmsSecondFactorAsync(user);          // 4.5 短信二次验证(开且绑手机→发码抛 40009 信令)
            var pair = await CreateTokenAsync(user);        // 5. 签发令牌
            await OnLoginSucceededAsync(user, pair);        // 6. 成功后置(登录日志/事件)
            return BuildLoginOutput(user, pair);            // 7. 组装出参
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
    /// 密码过期检查(§14,运行时可配 <c>sys.security.password.expireDays</c>,&lt;=0 关闭)。
    /// 不拦登录:过期仅置 <see cref="SysUser.MustChangePassword"/> 并落库,前端据登录出参强制跳转改密
    /// (与管理员重置密码同一信号,自助改密成功即清除并刷新 <see cref="SysUser.LastPasswordChangeTime"/>)。
    /// <para><see cref="SysUser.LastPasswordChangeTime"/> 为 null 的存量用户在此回填为当前时间——
    /// 过期窗口从升级后的首次登录起算,避免功能启用当天全量老用户被一起判过期。</para>
    /// </summary>
    protected virtual async Task CheckPasswordExpiryAsync(SysUser user)
    {
        var days = await policy.GetPasswordExpireDaysAsync();
        if (days <= 0) return;

        if (user.LastPasswordChangeTime is null)
        {
            user.LastPasswordChangeTime = DateTime.Now;
            await users.UpdateAsync(user);
            return;
        }

        if (!user.MustChangePassword && user.LastPasswordChangeTime.Value.AddDays(days) <= DateTime.Now)
        {
            user.MustChangePassword = true;
            await users.UpdateAsync(user);
        }
    }

    /// <summary>
    /// 短信二次验证检查(§14 登录加固):全局开关(<c>sys.security.mfa.enabled</c>)开且用户绑了手机号时,
    /// 创建挑战票据(绑定该 userId)并发码,抛 <see cref="ErrorCode.SmsCodeRequired"/>(40009)<b>信令</b>——
    /// 前端据 args 切验证码页,凭 <see cref="LoginBySmsChallengeAsync"/> 完成下半场。
    /// <para>无手机号的用户直通(全局开关不能锁死任何人——种子超管没有手机号);要强制全员 MFA 的消费方
    /// 覆写本步即可。40009 会被外层 catch 记为登录日志(审计"密码已过、待短信"),但不计失败锁定。</para>
    /// </summary>
    protected virtual async Task CheckSmsSecondFactorAsync(SysUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Phone) || !await smsOtp.IsMfaEnabledAsync()) return;

        var challengeId = await smsOtp.CreateMfaChallengeAsync(user.Id);
        var sent = await smsOtp.IssueAsync(ISmsOtpService.PURPOSE_MFA, challengeId, user.Phone);
        throw new AdminException(ErrorCode.SmsCodeRequired, new Dictionary<string, object?>
        {
            ["challengeId"] = challengeId,
            ["phoneMask"] = MaskPhone(user.Phone),
            ["expiresSeconds"] = sent.ExpiresSeconds,
            ["resendSeconds"] = sent.ResendSeconds,
        });
    }

    /// <inheritdoc />
    public virtual async Task<LoginOutput> LoginBySmsChallengeAsync(SmsChallengeLoginInput input)
    {
        SysUser? user = null;
        try
        {
            // 先验挑战再验码:码存在的前提是挑战曾存在,统一 40011 不泄露哪一环失效
            var userId = await smsOtp.GetMfaChallengeAsync(input.ChallengeId);
            AdminException.ThrowIf(userId == 0, ErrorCode.SmsCodeExpired);
            await smsOtp.VerifyAsync(ISmsOtpService.PURPOSE_MFA, input.ChallengeId, input.Code);

            // 码对才消费挑战(原子取删防并发重放);用户在挑战期间被删/停用则拒
            userId = await smsOtp.ConsumeMfaChallengeAsync(input.ChallengeId);
            AdminException.ThrowIf(userId == 0, ErrorCode.SmsCodeExpired);
            user = await users.GetByIdAsync(userId);
            AdminException.ThrowIf(user is null, ErrorCode.SmsCodeExpired);

            await CheckLoginPolicyAsync(user!);
            var pair = await CreateTokenAsync(user!);
            await OnLoginSucceededAsync(user!, pair);
            return BuildLoginOutput(user!, pair);
        }
        catch (AdminException ex)
        {
            await OnLoginFailedAsync(new LoginInput { Account = user?.Account ?? "" }, ex.Code);
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task<SmsSendOutput> ResendSmsChallengeAsync(SmsResendInput input)
    {
        // 持有活挑战即已过密码校验,无需图形验证码;冷却/日上限在 IssueAsync 内强制
        var userId = await smsOtp.GetMfaChallengeAsync(input.ChallengeId);
        AdminException.ThrowIf(userId == 0, ErrorCode.SmsCodeExpired);
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null || string.IsNullOrWhiteSpace(user!.Phone), ErrorCode.SmsCodeExpired);
        return await smsOtp.IssueAsync(ISmsOtpService.PURPOSE_MFA, input.ChallengeId, user!.Phone!);
    }

    /// <inheritdoc />
    public virtual async Task<SmsSendOutput> SendSmsLoginCodeAsync(PhoneCodeInput input)
    {
        AdminException.ThrowIf(!await smsOtp.IsLoginEnabledAsync(), ErrorCode.SmsLoginDisabled);
        await ValidateCaptchaAsync(new LoginInput { CaptchaId = input.CaptchaId, CaptchaCode = input.CaptchaCode });

        // 防枚举(§14):未命中"恰一个启用用户"(不存在/重复/停用)也走同闸门、同冷却、同出参,只是不发码。
        // 重复手机号因此静默不可用免密登录——防枚举优先,消费方需在录入侧保证手机号唯一。
        var phone = input.Phone.Trim();
        var matches = await users.AsQueryable().Where(u => u.Phone == phone && u.Enabled).Take(2).ToListAsync();
        return matches.Count == 1
            ? await smsOtp.IssueAsync(ISmsOtpService.PURPOSE_LOGIN, phone, phone)
            : await smsOtp.PretendIssueAsync(phone);
    }

    /// <inheritdoc />
    public virtual async Task<LoginOutput> LoginByPhoneAsync(PhoneLoginInput input)
    {
        var phone = input.Phone.Trim();
        try
        {
            AdminException.ThrowIf(!await smsOtp.IsLoginEnabledAsync(), ErrorCode.SmsLoginDisabled);
            // 未知/重复手机号从未存过码 → VerifyAsync 统一抛 40011,与"码过期"不可区分(防枚举)
            await smsOtp.VerifyAsync(ISmsOtpService.PURPOSE_LOGIN, phone, input.Code);

            var matches = await users.AsQueryable().Where(u => u.Phone == phone && u.Enabled).Take(2).ToListAsync();
            AdminException.ThrowIf(matches.Count != 1, ErrorCode.SmsCodeExpired);   // 发码后用户被停用/删除的窗口期防御
            var user = matches[0];

            await CheckLoginPolicyAsync(user);
            await CheckPasswordExpiryAsync(user);
            var pair = await CreateTokenAsync(user);
            await OnLoginSucceededAsync(user, pair);
            return BuildLoginOutput(user, pair);
        }
        catch (AdminException ex)
        {
            // 账号栏记手机号(免密流程没有账号输入);永不等于 PasswordWrong,不会误触失败锁定
            await OnLoginFailedAsync(new LoginInput { Account = phone }, ex.Code);
            throw;
        }
    }

    // ── 外部登录 / SSO(批次 D):模板方法,每步 protected virtual,消费者覆写解析/开户/绑定策略 ──

    private const string PROVISION_PWD_CHARS = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    /// <inheritdoc />
    public virtual async Task<LoginOutput> LoginByExternalAsync(ExternalLoginInput input)
    {
        // 外部登录依赖三件可选注入(provider 集合 / 绑定服务 / RBAC);DI 下必然齐备,但手工构造 AuthService 且
        // 省略了这些参数的消费者子类会走到这——给出明确"该能力未接线"信号(40013),而非后续裸 NRE。
        AdminException.ThrowIf(externalProviders is null || externalBindings is null || rbac is null, ErrorCode.OAuthProviderDisabled);
        SysUser? user = null;
        try
        {
            var identity = await ResolveExternalIdentityAsync(input);   // 1. provider 换外部身份(含存在/启用校验)
            user = await ResolveExternalUserAsync(identity);            // 2. 找绑定 / 按策略开户 or 拒绝
            await CheckLoginPolicyAsync(user);                          // 3. 停用检查(复用)
            var pair = await CreateTokenAsync(user);                    // 4. 签发令牌 + 开会话(复用)
            await OnLoginSucceededAsync(user, pair);                    // 5. 成功后置(复用)
            return BuildLoginOutput(user, pair);                        // 6. 出参(复用)
        }
        catch (AdminException ex)
        {
            // 账号栏记已解析账号或 external:{provider}(尚未解析时);永不等于 PasswordWrong,不触发失败锁定
            await OnLoginFailedAsync(new LoginInput { Account = user?.Account ?? $"external:{input.ProviderCode}" }, ex.Code);
            throw;
        }
    }

    /// <summary>解析外部身份:按 code 选 provider(不存在/被运营关掉抛 40013),调其 ExchangeAsync 换身份。</summary>
    protected virtual async Task<ExternalIdentity> ResolveExternalIdentityAsync(ExternalLoginInput input)
    {
        var provider = externalProviders?.FirstOrDefault(p => p.Code == input.ProviderCode);
        AdminException.ThrowIf(provider is null, ErrorCode.OAuthProviderDisabled);
        AdminException.ThrowIf(!await externalBindings!.IsEnabledAsync(input.ProviderCode), ErrorCode.OAuthProviderDisabled);
        return await provider!.ExchangeAsync(new ExternalExchangeRequest(input.Code, input.CodeVerifier, input.Nonce, input.RedirectUri));
    }

    /// <summary>外部身份 → 本地用户:有绑定取之;无绑定按 provider 运营策略(默认拒绝抛 40016,或自动开户)。</summary>
    protected virtual async Task<SysUser> ResolveExternalUserAsync(ExternalIdentity identity)
    {
        var binding = await externalBindings!.FindByExternalAsync(identity.Provider, identity.Subject);
        if (binding is not null)
        {
            var bound = await users.GetByIdAsync(binding.UserId);
            AdminException.ThrowIf(bound is null, ErrorCode.OAuthAccountNotBound);   // 悬挂绑定(用户已删)→ 当未绑定拒绝
            return bound!;
        }

        var unbound = await externalBindings!.GetUnboundPolicyAsync(identity.Provider);
        if (unbound != ExternalUnboundPolicy.Provision)
            throw new AdminException(ErrorCode.OAuthAccountNotBound);
        return await ProvisionExternalUserAsync(identity);
    }

    /// <summary>自动开户:建本地账号(随机口令占位、免改密)+ 落默认角色/机构 + 写绑定,同事务;失败整体回滚。</summary>
    protected virtual async Task<SysUser> ProvisionExternalUserAsync(ExternalIdentity identity)
    {
        var (roleIds, orgId) = await externalBindings!.GetProvisionDefaultsAsync(identity.Provider);
        var user = new SysUser
        {
            Account = await GenerateProvisionAccountAsync(identity),
            // 占位强随机口令:外部登录不走密码,但库不允空密码(与 SMS-only 用户同口径)
            Password = hasher.Hash(RandomNumberGenerator.GetString(PROVISION_PWD_CHARS, 24)),
            Name = identity.DisplayName ?? identity.Subject,
            Email = identity.Email,
            Phone = identity.Phone,
            Enabled = true,
            IsSuperAdmin = false,
            MustChangePassword = false,          // 外部登录用户不需改密
            LastPasswordChangeTime = DateTime.Now,
            OrgId = orgId,
        };

        var tran = await users.Db.Ado.UseTranAsync(async () =>
        {
            await users.InsertAsync(user);       // AOP 回填雪花 Id
            if (roleIds.Count > 0) await rbac!.SetUserRolesAsync(user.Id, roleIds);
            await externalBindings!.BindAsync(user.Id, identity);
        });
        if (!tran.IsSuccess)
        {
            // 并发首登竞态:另一路已抢先给同一外部身份开好户并绑定,本路事务撞唯一约束整体回滚(无孤儿用户)。
            // 改用对方已建的账号 → 让并发首登幂等,而非把裸库唯一约束异常甩成 500(批次 D 复查 L1)。
            var raced = await externalBindings!.FindByExternalAsync(identity.Provider, identity.Subject);
            if (raced is not null && await users.GetByIdAsync(raced.UserId) is { } winner)
                return winner;
            throw tran.ErrorException;   // 非竞态的真失败(rbac/DB 等):原样抛,交由外层记日志 + 500
        }
        return user;
    }

    /// <summary>为自动开户生成本地账号:优先取邮箱前缀/显示名清洗为 slug,回退 subject;含软删查重,撞名追加随机后缀。</summary>
    protected virtual async Task<string> GenerateProvisionAccountAsync(ExternalIdentity identity)
    {
        var seed = identity.Email?.Split('@')[0] ?? identity.DisplayName ?? identity.Subject;
        var slug = new string((seed ?? "user").Where(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.').ToArray());
        if (slug.Length == 0) slug = "user";
        var baseAccount = $"{identity.Provider}_{slug}";
        if (baseAccount.Length > 50) baseAccount = baseAccount[..50];

        var account = baseAccount;
        for (var i = 0; i < 5; i++)
        {
            // 查重把软删行也纳入(ClearFilter<ISoftDelete>)——防御性:软删走 repo.DeleteAsync 时 Account 已被
            // 追加 _del_{id} 后缀释放唯一位(见 SqlSugarRepository.DeleteAsync),精确等值本不会命中软删行;
            // 保留此过滤只为兜住"绕过回收直接置 IsDelete"的边角软删,避免撞库唯一约束抛原生 500。
            if (!await users.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(u => u.Account == account))
                return account;
            account = $"{baseAccount}_{RandomNumberGenerator.GetString(PROVISION_PWD_CHARS, 4)}";
        }
        return account;   // 5 次仍撞(近乎不可能):交由库唯一约束兜底
    }

    /// <summary>手机号打码(40009 信令给前端展示"码已发至 138****1234");过短的号只留尾 2 位。</summary>
    protected virtual string MaskPhone(string phone) =>
        phone.Length >= 8 ? $"{phone[..3]}****{phone[^4..]}" : $"****{phone[^Math.Min(2, phone.Length)..]}";

    /// <summary>
    /// 签发令牌 + 开会话(设计 §15)。SessionId 用 GUID v7(时间有序,BCL 内置)——在线用户与强退的稳定锚点;
    /// <see cref="ISessionService.OpenAsync"/> 负责落库/缓存会话、存刷新令牌哈希、执行单端/限并发策略。
    /// </summary>
    protected virtual async Task<TokenPair> CreateTokenAsync(SysUser user)
    {
        var sessionId = Guid.CreateVersion7().ToString("N");
        var (accessMin, refreshMin) = await policy.GetSessionTtlAsync();   // 令牌时长运行时可配
        var pair = tokens.Create(new TokenSubject(user.Id, user.Account, sessionId, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(accessMin), TimeSpan.FromMinutes(refreshMin));
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
        MustChangePassword = user.MustChangePassword,   // 不拦登录,仅透传给前端强制跳转改密
    };
}
