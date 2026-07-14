using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 中间件挂载点(设计 §12/§14):三行零配置宿主的 <c>MapTenonAdmin</c> 只拿到 <c>IEndpointRouteBuilder</c>、
/// 无处插中间件。用 <see cref="IStartupFilter"/> 在管道前段注入需要中间件的横切能力(转发头 + CORS + 限流),
/// 用户仍只写三行、无需手动 <c>UseForwardedHeaders</c>/<c>UseCors</c>/<c>UseRateLimiter</c>。
/// <para>次序有硬约束:
/// <b>①转发头</b>必须<b>最先</b>——它重写 <c>Connection.RemoteIpAddress</c>,晚一步下游(限流分区、日志 IP)
/// 读到的就还是代理 IP;
/// <b>②CORS</b>(预检 OPTIONS 先于一切放行);
/// <b>③限流</b>(尽早挡洪泛,省下游开销,且此时 IP 已是真实客户端)。</para>
/// <para>后两者应用的都是<b>全局策略</b>(CORS 命名默认策略 / 限流全局分区器按 <c>Request.Path</c> 区分认证端点),
/// 不依赖端点元数据,故置于 UseRouting 之前也正确。认证/授权中间件由 WebApplication 在其后自动插入,次序不冲突。</para>
/// </summary>
internal sealed class TenonAdminMiddlewareStartupFilter(AdminApiOptions api) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        // 默认关:不在代理后面时启用它 = 允许任何人经 X-Forwarded-For 伪造自己的 IP。
        // 受信来源在 AddTenonAdmin 里绑进 ForwardedHeadersOptions(未声明受信来源时那里已 fail-fast)。
        if (api.ForwardedHeaders.Enabled) app.UseForwardedHeaders();
        app.UseCors(TenonAdminSetup.CorsPolicyName);
        app.UseMiddleware<RateLimitMiddleware>();
        next(app);
    };
}
