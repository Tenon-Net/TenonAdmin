using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 用户侧入口(设计 §3.1):<c>AddTenonAdmin()</c> + <c>MapTenonAdmin()</c>,三行 Program.cs 起全站。
/// <para>认证/授权中间件无需用户手动 Use——WebApplication 检测到认证服务注册后自动插入管道。</para>
/// </summary>
public static class TenonAdminSetup
{
    /// <summary>内置 CORS 命名策略名(由 <see cref="TenonAdminMiddlewareStartupFilter"/> 在管道前段应用)</summary>
    public const string CorsPolicyName = "TenonAdmin";

    public static IServiceCollection AddTenonAdmin(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TenonAdminOptions>? configure = null)
    {
        // ── 配置:json 绑定(缺省即默认值,零配置可跑)→ 代码覆写 → Options 对象直接入容器 ──
        var options = new TenonAdminOptions();
        configuration.GetSection("TenonAdmin").Bind(options);
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(options.Database);
        services.AddSingleton(options.Cache);
        services.AddSingleton(options.Jwt);
        services.AddSingleton(options.Security);
        services.AddSingleton(options.Upload);
        services.AddSingleton(options.Api);
        services.AddSingleton(options.Id);
        services.TryAddSingleton(TimeProvider.System);          // 统一时间源(§12),测试可换 Fake

        // ── 当前用户 + 数据范围环境(§6):HTTP 侧实现在此先注册,压过 SqlSugar 层的 AsyncLocal 兜底 ──
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ICurrentUser, HttpContextCurrentUser>();
        // HttpContext.Items 版数据范围载体(避免授权过滤器里 AsyncLocal 不回流的陷阱);非 HTTP 场景回退 AsyncLocal
        services.TryAddSingleton<IDataScopeContext, HttpContextDataScopeContext>();

        // ── 数据层 + 领域服务(实体程序集在此登记,§5.7 注册模型)──────────────
        //   实体扫描 = 内置 Services + 用户显式登记的业务程序集,让用户实体也 CodeFirst 建表
        var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
        entityAssemblies.AddRange(options.ApplicationAssemblies);
        services.AddTenonAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()]);
        services.AddTenonAdminServices();

        // ── JWT:签名密钥惰性解析一次(生产缺配 fail-fast;开发密钥持久化 + 警告),签发与验证共用同一实例 ──
        services.TryAddSingleton(sp =>
            JwtKeyResolver.Resolve(options.Jwt,
                sp.GetRequiredService<IHostEnvironment>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtKeyResolver))));
        services.TryAddSingleton<ITokenProvider>(sp => new JwtTokenProvider(
            options.Jwt, sp.GetRequiredService<SymmetricSecurityKey>(), sp.GetRequiredService<TimeProvider>()));

        // 权限码提供者由 Services 层的 RbacPermissionProvider 提供(RBAC 真实现,§6);
        // 用户前置注册同接口实现(如对接外部鉴权中心)即整体替换(§5.2)。

        // ── 认证/授权:微软官方 JwtBearer(§2.2 替代表)────────────────────────
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // 验证参数经 Options 框架注入签名密钥——与签发端共享同一单例,无静态桥
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SymmetricSecurityKey>((o, signingKey) =>
            {
                o.MapInboundClaims = false;                     // 保留原始 claim 名(sub/sid/sadm),不做遗留映射
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Jwt.Issuer,
                    IssuerSigningKey = signingKey,
                    ValidateAudience = false,                   // 单体管理后台,不启用 audience 维度
                    ValidateLifetime = true,                    // 显式:校验 exp/nbf
                    ClockSkew = TimeSpan.FromSeconds(30),       // 收紧默认 5 分钟宽限,贴合短命令牌策略(P2-5)
                    NameClaimType = JwtRegisteredClaimNames.UniqueName, // User.Identity.Name = 登录账号(走常量,不写死字面量,P2-7)
                };
                // 未认证/令牌过期的框架 401 challenge 也套统一信封(40006),与 [RolePermission]/[ActiveSession] 一致(P1-2)
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.Response.ContentType = "application/json";
                        await ctx.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.TokenInvalid));
                    },
                };
            });
        // 默认拒绝走 MapControllers().RequireAuthorization()(见 MapTenonAdmin),只作用于真实控制器端点、
        // 尊重 [AllowAnonymous],且不影响未匹配路由的 404(FallbackPolicy 会把 404 劫持成 401,故不用它)。
        services.AddAuthorization();

        // ── MVC 控制器:本程序集作为 ApplicationPart 挂入宿主 ──
        //   全局过滤器:业务异常 → 统一信封;操作日志(只记 [OperationLog] 动作,§4/T6);
        //   裸返回兜底包信封(用户控制器 return dto 即得 Result<T>,§12/T8)
        var mvc = services.AddControllers(o =>
            {
                o.Filters.Add<AdminExceptionFilter>();
                o.Filters.Add<OperationLogFilter>();
                o.Filters.Add<ResultEnvelopeFilter>();
                o.Conventions.Add(new DisabledModuleConvention(options.Api));   // 按配置摘除禁用模块的控制器(§5.4)
            })
            .AddApplicationPart(typeof(TenonAdminSetup).Assembly);   // 内置控制器
        // 用户业务程序集里的控制器也挂进来,与内置控制器同管道(§5.7)
        foreach (var assembly in options.ApplicationAssemblies.Distinct())
            mvc.AddApplicationPart(assembly);

        // ── CORS(§12/§14):命名策略,默认收紧(无源=不放行);经 IStartupFilter 挂载,保三行零配置宿主 ──
        services.AddCors(o => o.AddPolicy(CorsPolicyName, p =>
        {
            if (options.Api.Cors.AllowedOrigins.Length > 0)
            {
                p.WithOrigins(options.Api.Cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
                if (options.Api.Cors.AllowCredentials) p.AllowCredentials();
            }
            // 空源 → 空策略:不放行任何跨源(生产必须显式配置)
        }));
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, TenonAdminMiddlewareStartupFilter>());

        // ── 限流(§12/§14):按客户端 IP 固定窗口,认证端点(/api/v1/auth/*)更严;经上面的 IStartupFilter 挂 UseRateLimiter ──
        //   限流器在路由前运行,按 Request.Path 直接区分认证端点(不依赖端点元数据),命中即 429 + 统一信封(40008)。
        var rl = options.Security.RateLimit;
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? (int)ra.TotalSeconds : rl.WindowSeconds;
                ctx.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ctx.HttpContext.Response.ContentType = "application/json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    Result<object>.Fail(ErrorCode.TooManyRequests, new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfter }), ct);
            };
            if (rl.Enabled)
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                {
                    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var isAuth = ctx.Request.Path.StartsWithSegments("/api/v1/auth");
                    var permit = isAuth ? rl.AuthPermitPerWindow : rl.PermitPerWindow;
                    if (permit <= 0) return RateLimitPartition.GetNoLimiter("nolimit");
                    return RateLimitPartition.GetFixedWindowLimiter(
                        (isAuth ? "auth:" : "all:") + ip,
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromSeconds(rl.WindowSeconds), QueueLimit = 0 });
                });
        });

        // ── 内置 OpenAPI 文档(§13.6 契约源)+ 健康检查(§12:/health 存活 + /health/ready 依赖就绪)──
        services.AddOpenApi();          // 产出 /openapi/v1.json;内置控制器显式 Result<T> → 契约含信封(裸返回端点见 ResultEnvelopeFilter 契约提示)
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("db", tags: ["ready"])
            .AddCheck<CacheHealthCheck>("cache", tags: ["ready"]);

        return services;
    }

    public static IEndpointRouteBuilder MapTenonAdmin(this IEndpointRouteBuilder endpoints)
    {
        // 内置控制器路由(认证、探针;后续模块的控制器自动包含)。
        // 默认拒绝(§14/P2-6):所有控制器端点强制认证,[AllowAnonymous](登录/刷新/验证码/自定义匿名控制器)显式豁免;
        // 漏挂 [RolePermission] 的 action 也不再静默公开。只作用于真实端点,不劫持未匹配路由的 404。
        endpoints.MapControllers().RequireAuthorization();

        // OpenAPI 文档:仅开发环境暴露(生产匿名开放会泄露完整 API 契约作侦察面,P2-8);
        // 匿名可访问(否则被上面的 FallbackPolicy 挡成 401)。§13.6 契约源本就是开发期前端代码生成用。
        var env = endpoints.ServiceProvider.GetService<IHostEnvironment>();
        if (env is null || env.IsDevelopment())
            endpoints.MapOpenApi().AllowAnonymous();

        // 健康检查(§12):/health 只报进程存活(不跑依赖检查),/health/ready 探 DB/缓存就绪。
        // 匿名(供编排层 liveness/readiness 探针),不受默认拒绝约束。
        endpoints.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();
        return endpoints;
    }
}
