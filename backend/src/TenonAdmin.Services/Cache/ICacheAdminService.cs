namespace TenonAdmin.Services;

/// <summary>
/// 缓存管理服务(设计 §4 运维)——面向管理员的<b>定向失效</b>动作。补自动失效(授权变更时 <c>RbacService</c>
/// 已自动 bump 门户代际 + 按用户清 perm/scope 键)覆盖不到的场景:有人<b>直接改库</b>致缓存陈旧的运维逃生舱。
/// <para>刻意<b>只清不看</b>:缓存值里有明文 OTP/验证码、键里内嵌手机号/IP(见 <see cref="TenonAdmin.Core.CacheKeys"/>),
/// 故不提供键浏览/取值端点——列键即泄 PII、读值等于旁路读 OTP。全部动作都是单键 <c>RemoveAsync</c>/<c>IncrementAsync</c>,
/// 由 DB 已知 ID 驱动、零缓存枚举,内存 / Redis provider 皆可用。</para>
/// <para>注册 <c>TryAddScoped</c>,方法 <c>virtual</c> 可覆写(如接自有缓存命名空间)。</para>
/// </summary>
public interface ICacheAdminService
{
    /// <summary>清全部用户的权限 / 数据范围缓存(遍历用户表逐键移除)。返回被清理的用户数。</summary>
    Task<int> FlushAuthAsync(CancellationToken cancellationToken = default);

    /// <summary>清全部字典项缓存(按字典类型码)。返回被清理的字典类型数。</summary>
    Task<int> FlushDictAsync(CancellationToken cancellationToken = default);

    /// <summary>清全部系统配置缓存(按配置键)。返回被清理的配置项数。</summary>
    Task<int> FlushConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>递增门户代际,令所有用户的门户模块 / 菜单树缓存整体惰性失效(O(1))。返回新代际值。</summary>
    Task<long> RebuildPortalAsync(CancellationToken cancellationToken = default);
}
