using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>未绑定外部身份的处理策略(批次 D,Q1 = 可配置、默认拒绝)。</summary>
public enum ExternalUnboundPolicy
{
    /// <summary>拒绝:首次外部登录须已有本地账号并在个人中心绑定(安全默认,契合"账号只由管理员开")</summary>
    Reject = 0,

    /// <summary>自动开户:首次外部登录建本地账号(落配置的默认角色 / 机构)</summary>
    Provision = 1,
}

/// <summary>
/// 用户外部身份绑定服务(批次 D)。管 <c>sys_user_external</c> 表的增删查,并<b>收口</b>"运营配置"读取——
/// 启用开关 / 未绑定策略 / 开户默认角色·机构,统一走 <c>sys_config</c> 键
/// <c>sys.externalauth.{code}.{enabled|provisioning|defaultRoleIds|defaultOrgId}</c>(运行时可改、无需重启)。
/// <para>方法 <c>virtual</c>,<c>TryAddScoped</c> 注册。控制器与 AuthService 都只调本服务,不各自散读 sys_config 键。</para>
/// </summary>
public interface ISysUserExternalService
{
    /// <summary>按外部身份(<c>(provider, subject)</c> 唯一)找绑定;无则 null。</summary>
    Task<SysUserExternal?> FindByExternalAsync(string provider, string subject);

    /// <summary>列出某用户的所有外部绑定(个人中心"账号绑定"用)。</summary>
    Task<List<SysUserExternal>> ListByUserAsync(long userId);

    /// <summary>把某本地用户绑定到一个外部身份;该外部身份已被他人占用、或该用户已绑过同 provider,抛 <see cref="ErrorCode.OAuthAlreadyBound"/>。</summary>
    Task BindAsync(long userId, ExternalIdentity identity);

    /// <summary>解绑某用户在某 provider 下的绑定(不存在则静默返回)。</summary>
    Task UnbindAsync(long userId, string provider);

    /// <summary>该 provider 是否启用(键 <c>sys.externalauth.{code}.enabled</c>,缺省 true = 配了连接即可用,此键作运行时 kill-switch)。</summary>
    Task<bool> IsEnabledAsync(string provider);

    /// <summary>该 provider 的未绑定策略(键 <c>...provisioning</c> = <c>reject|provision</c>,缺省 <see cref="ExternalUnboundPolicy.Reject"/>)。</summary>
    Task<ExternalUnboundPolicy> GetUnboundPolicyAsync(string provider);

    /// <summary>自动开户默认值(角色 Id 列表取 <c>...defaultRoleIds</c>=CSV,机构取 <c>...defaultOrgId</c>)。</summary>
    Task<(IReadOnlyCollection<long> RoleIds, long? OrgId)> GetProvisionDefaultsAsync(string provider);
}
