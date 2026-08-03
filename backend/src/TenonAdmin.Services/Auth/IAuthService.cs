namespace TenonAdmin.Services;

/// <summary>
/// 认证服务(设计 §4 认证模块)。默认实现 <see cref="AuthService"/> 是"模板方法可覆写"
/// 的招牌样板(设计 §5.3)——登录长流程拆成小步,每步 protected virtual,
/// 对接 LDAP/AD/外部 SSO 只需继承覆写其中一步。
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 账密登录,成功返回令牌对;任何业务失败抛 <see cref="Core.AdminException"/>(40xxx 段)。
    /// 短信二次验证启用且用户绑了手机号时,密码校验通过后抛 40009 信令(args 带挑战 Id),
    /// 凭 <see cref="LoginBySmsChallengeAsync"/> 完成登录。
    /// </summary>
    Task<LoginOutput> LoginAsync(LoginInput input);

    /// <summary>短信二次验证:凭挑战 Id + 短信码完成登录(40009 信令的下半场);失败抛 40010/40011。</summary>
    Task<LoginOutput> LoginBySmsChallengeAsync(SmsChallengeLoginInput input);

    /// <summary>短信二次验证:重发验证码(冷却/日上限内置);挑战失效抛 40011。</summary>
    Task<SmsSendOutput> ResendSmsChallengeAsync(SmsResendInput input);

    /// <summary>
    /// TOTP 二次验证:凭挑战 Id(登录 40018 信令)+ 动态口令完成登录。
    /// 码错抛 40019;未绑定抛 40020。
    /// </summary>
    Task<LoginOutput> LoginByTotpChallengeAsync(TotpChallengeLoginInput input);

    /// <summary>免密登录:向手机号发送登录验证码(开关关抛 40012;响应刻意不区分手机号是否存在,防枚举)。</summary>
    Task<SmsSendOutput> SendSmsLoginCodeAsync(PhoneCodeInput input);

    /// <summary>免密登录:手机号 + 短信码换令牌(开关关抛 40012;码错/失效抛 40010/40011)。</summary>
    Task<LoginOutput> LoginByPhoneAsync(PhoneLoginInput input);

    /// <summary>
    /// 外部登录(OAuth/SSO 回调,批次 D):用授权码换外部身份 → 找绑定 →(未绑定按 provider 策略 拒绝/开户)→
    /// 复用建会话发令牌那一步。无此 provider / 被运营关掉抛 40013,换取失败抛 40015,未绑定且策略=拒绝抛 40016。
    /// </summary>
    Task<LoginOutput> LoginByExternalAsync(ExternalLoginInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 已解析的外部身份登录(跳过 Exchange)。HTTP 回调在授权码已消费后走此路径;
    /// 未绑定且策略=拒绝仍抛 40016,由控制器转为 pending-link。
    /// </summary>
    Task<LoginOutput> LoginByExternalIdentityAsync(Core.ExternalIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>用刷新令牌换发新令牌对(轮换 + 复用检测,设计 §15);失败抛 40007。</summary>
    Task<LoginOutput> RefreshAsync(RefreshInput input);

    /// <summary>登出:吊销指定会话(原访问令牌下次请求即 401)。</summary>
    Task LogoutAsync(string sessionId);
}
