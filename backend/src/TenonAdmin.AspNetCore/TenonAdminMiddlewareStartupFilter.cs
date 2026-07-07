using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 中间件挂载点(设计 §12/§14):三行零配置宿主的 <c>MapTenonAdmin</c> 只拿到 <c>IEndpointRouteBuilder</c>、
/// 无处插中间件。用 <see cref="IStartupFilter"/> 在管道前段注入需要中间件的横切能力(CORS + 限流),
/// 用户仍只写三行、无需手动 <c>UseCors</c>/<c>UseRateLimiter</c>。
/// <para>次序:先 <b>CORS</b>(预检 OPTIONS 先于一切放行),再 <b>RateLimiter</b>(尽早挡洪泛,省下游开销)。
/// 两者应用的都是<b>全局策略</b>(CORS 命名默认策略 / 限流全局分区器按 <c>Request.Path</c> 区分认证端点),
/// 不依赖端点元数据,故置于 UseRouting 之前也正确。认证/授权中间件由 WebApplication 在其后自动插入,次序不冲突。</para>
/// </summary>
internal sealed class TenonAdminMiddlewareStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseCors(TenonAdminSetup.CorsPolicyName);
        app.UseRateLimiter();
        next(app);
    };
}
