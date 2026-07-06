using Microsoft.AspNetCore.Http;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// HTTP 环境下的 <see cref="IDataScopeContext"/> 实现:用 <c>HttpContext.Items</c> 存当前请求的生效范围。
/// <para>为什么不用 AsyncLocal:授权过滤器是 MVC 管道的被调用方,其内部 <c>await</c> 之后设置的 AsyncLocal
/// 不会回流到管道上游(经典陷阱),动作里的查询将读不到。<c>HttpContext.Items</c> 挂在请求对象上、
/// 全管道稳定可见,无此问题。非 HTTP 场景(自检/后台)回退到 SqlSugar 层的 AsyncLocal 实现。</para>
/// <para>未设置(如匿名请求/无 HttpContext)返回 <see cref="DataScopeResult.Unrestricted"/>——
/// 但受保护业务接口都会在授权管道显式解析并写入范围,故认证请求恒被正确约束。</para>
/// </summary>
public sealed class HttpContextDataScopeContext(IHttpContextAccessor accessor) : IDataScopeContext
{
    private static readonly object ITEM_KEY = new();

    public DataScopeResult Current
    {
        get => accessor.HttpContext?.Items.TryGetValue(ITEM_KEY, out var v) == true && v is DataScopeResult r
            ? r
            : DataScopeResult.Unrestricted;
        set
        {
            var ctx = accessor.HttpContext;
            if (ctx is not null) ctx.Items[ITEM_KEY] = value;
        }
    }
}
