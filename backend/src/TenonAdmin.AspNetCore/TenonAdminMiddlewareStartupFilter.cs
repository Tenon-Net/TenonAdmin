using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 中间件挂载点(设计 §12/§14):三行零配置宿主的 <c>MapTenonAdmin</c> 只拿到 <c>IEndpointRouteBuilder</c>、
/// 无处插中间件。用 <see cref="IStartupFilter"/> 在管道前段注入需要中间件的横切能力(当前:CORS 全局命名策略),
/// 用户仍只写三行、无需手动 <c>UseCors</c>。
/// <para>CORS 置于最前:预检 OPTIONS 请求先于认证处理;应用的是<b>命名默认策略</b>(<see cref="TenonAdminSetup.CorsPolicyName"/>),
/// 不依赖端点元数据,故置于 UseRouting 之前也正确。后续 RateLimiter 等中间件可复用此挂载点(Phase 2b)。</para>
/// </summary>
internal sealed class TenonAdminMiddlewareStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseCors(TenonAdminSetup.CorsPolicyName);
        next(app);
    };
}
