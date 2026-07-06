namespace TenonAdmin.Core;

/// <summary>
/// 权限码提供者(扩展点,设计 §5.5/§6)。
/// <para>本框架的权限模型:<b>权限码 = 规范化路由(含 HTTP Method)</b>,
/// 形如 <c>POST:/api/v1/biz/device/add</c>——不手写 <c>"sys:user:add"</c> 之类的魔法字符串。
/// 授权过滤器对标了 <c>[RolePermission]</c> 的接口,取当前用户的权限码集合判断是否包含当前路由。</para>
/// <para>默认实现从缓存读取(登录/授权变更时由 RBAC 服务写入);
/// 典型替换场景:对接外部鉴权中心、按租户/环境动态计算。</para>
/// </summary>
public interface IPermissionProvider
{
    /// <summary>
    /// 取用户当前生效的权限码集合。实现应保证低延迟(走缓存,不查库),
    /// 该方法处于每个受保护请求的热路径上。
    /// </summary>
    Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(long userId, CancellationToken cancellationToken = default);
}
