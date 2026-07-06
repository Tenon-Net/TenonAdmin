namespace TenonAdmin.Services;

/// <summary>
/// 认证服务(设计 §4 认证模块)。默认实现 <see cref="AuthService"/> 是"模板方法可覆写"
/// 的招牌样板(设计 §5.3)——登录长流程拆成小步,每步 protected virtual,
/// 对接 LDAP/AD/外部 SSO 只需继承覆写其中一步。
/// </summary>
public interface IAuthService
{
    /// <summary>账密登录,成功返回令牌对;任何业务失败抛 <see cref="Core.AdminException"/>(40xxx 段)</summary>
    Task<LoginOutput> LoginAsync(LoginInput input);
}
