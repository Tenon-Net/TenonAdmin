namespace TenonAdmin.Core;

/// <summary>
/// 数据范围解析器(扩展点,设计 §5.5/§6)——把某用户的多角色范围合并成一个 <see cref="DataScopeResult"/>。
/// <para>默认实现聚合用户角色的 <c>sys_role_data_scope</c> 配置(All 最宽优先;本机构及以下按机构树展开子孙),
/// 结果按用户缓存(授权/机构变更时失效)。典型替换:自定义隔离维度(如租户)。</para>
/// </summary>
public interface IDataScopeProvider
{
    /// <summary>解析指定用户当前生效的数据范围(走缓存,未命中才聚合查库)。</summary>
    Task<DataScopeResult> ResolveAsync(long userId, CancellationToken cancellationToken = default);
}
