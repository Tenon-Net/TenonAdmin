using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
        // 上传根按 ContentRoot 解析(与 SQLite 库文件、JWT 开发密钥同一基准),不按进程 CWD。
        // 三条可写路径若基准不一致,容器/服务托管里 CWD 一变(entrypoint 脚本 cd 过、k8s 覆写 workingDir、
        // 从别处 `dotnet /app/App.dll`),数据卷和上传卷就悄悄分家:库还在,文件"消失"。绝对路径原样保留。
        // 就地改写同一个 options.Upload 实例,故 TenonAdminOptions.Upload 读到的也是解析后的值。
        services.AddSingleton(sp =>
        {
            var contentRoot = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
            options.Upload.RootPath = Path.GetFullPath(Path.Combine(contentRoot, options.Upload.RootPath));
            return options.Upload;
        });
        services.AddSingleton(options.Api);
        services.AddSingleton(options.Email);   // 邮件通道配置(SMTP 主机为空则走日志实现,见 ServicesSetup 的 IEmailSender 工厂)
        services.AddSingleton(options.ExternalAuth);   // 外部登录 / SSO 配置(内置 OIDC provider 列表 + 回调基址,见批次 D)

        // ── 雪花机器号(§12):多副本同号 = 同毫秒发号撞主键。这是数据损坏级的问题,而它今天静默发生 ──
        //   没有可靠的"我是不是多副本"信号,但选了 Redis 缓存基本等同于宣告多实例意图
        //   (进程内缓存在多副本下根本不成立:强退失效、权限陈旧、锁定计数翻倍)。
        //   故 Redis + 未显式给机器号 → 启动即抛。显式写 0 即视为知情,放行。
        if (string.Equals(options.Cache.Provider, "Redis", StringComparison.OrdinalIgnoreCase) && options.Id.WorkerId is null)
            throw new InvalidOperationException(
                "已配置 Redis 缓存(多实例部署),但未显式设置 TenonAdmin:Id:WorkerId。" +
                "水平扩展时每个实例必须配一个互不相同的机器号(0–63),否则不同实例同毫秒发号会撞主键。" +
                "单实例请显式配 0 以示知情;k8s 可用 StatefulSet 的 Pod 序号注入。");
        services.AddSingleton(options.Id);
        services.TryAddSingleton(TimeProvider.System);          // 统一时间源(§12),测试可换 Fake

        // ── 文件日志:默认关。开了才长出 logs/{级别}/{日期}.log ──────────────────
        //   ILogger 默认只有 Console provider,进程一重启异常堆栈就没了;而运行时依赖红线(只准 SqlSugarCore + Microsoft.*)
        //   把 Serilog 挡在外面,故内核自带一个(FileLoggerProvider)。它是加法不排他:消费者照常 AddSerilog(),两者并存。
        services.AddSingleton(options.Logging);
        if (options.Logging.File.Enabled)
        {
            // 两个泛型参数缺一不可:TryAddEnumerable 要从工厂的返回类型认出实现类型才能去重,
            // 只写 <ILoggerProvider> 会因"与其他 ILoggerProvider 注册无从区分"在启动时抛。
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>(sp =>
            {
                var env = sp.GetRequiredService<IWebHostEnvironment>();
                // 与上传根、SQLite 库文件同一基准:相对路径按 ContentRoot 解析,不随进程 CWD 漂移
                var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Logging.File.Path));

                // 红线:日志落在静态根下 = UseStaticFiles() 把异常堆栈、请求参数、内部路径匿名直出。
                // 同 Upload.RootPath 的老坑,但那个至少还要猜文件名——日志的路径是可枚举的。宁可启动就炸。
                //
                // WebRootPath 在 wwwroot **目录尚不存在**时是 null —— 直接信它,守卫就会在最该管用的时候失灵:
                // 全新部署的机器上 wwwroot 往往还没有,于是守卫跳过、日志照写进 wwwroot/logs;等第一次上传时
                // LocalFileStorage 把 wwwroot 建出来,静态中间件就开始把整个日志目录匿名直出。
                // 所以判据是「这个路径会不会落进静态根」,而不是「静态根现在存不存在」:目录缺失时回退到默认的 wwwroot 名。
                var webRoot = string.IsNullOrEmpty(env.WebRootPath)
                    ? Path.Combine(env.ContentRootPath, "wwwroot")
                    : env.WebRootPath;
                if (root.StartsWith(Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"TenonAdmin:Logging:File:Path 解析到 {root},位于 wwwroot 内。" +
                        "宿主一旦 UseStaticFiles() 托管前端产物,整个日志目录就会被匿名直出(异常堆栈、请求参数、内部路径全在里面)。请把它挪出静态根。");

                return new FileLoggerProvider(root, options.Logging.File, options.Id.WorkerId ?? 0,
                    sp.GetRequiredService<TimeProvider>());
            }));
        }

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

        // 文件直链签名(§14):从 JWT 密钥派生子密钥,签出匿名可访问但不可伪造的 /view 链接
        //(<img src> 带不了 Authorization 头 —— 通知正文里的图片全靠它)
        services.TryAddSingleton<IFileUrlSigner>(sp => new FileUrlSigner(sp.GetRequiredService<SymmetricSecurityKey>()));

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

        // ── 外部登录 / SSO(批次 D):按 appsettings 装内置 OIDC provider(零新包);未配则整段跳过 ──
        services.AddExternalAuthProviders(options.ExternalAuth);

        // ── MVC 控制器:本程序集作为 ApplicationPart 挂入宿主 ──
        //   全局过滤器:业务异常 → 统一信封;操作日志(默认记一切写操作,读操作/匿名端点除外,§4/T6);
        //   裸返回兜底包信封(用户控制器 return dto 即得 Result<T>,§12/T8)
        var mvc = services.AddControllers(o =>
            {
                if (options.DemoMode)
                    o.Filters.Add<DemoModeFilter>();
                o.Filters.Add<AdminExceptionFilter>();
                o.Filters.Add<ExceptionLogFilter>();   // 未捕获异常旁路留痕(不吞异常,500 照旧);业务异常显式跳过
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

        // ── 反向代理转发头(§14):反代之后 RemoteIpAddress 是代理的 IP,不解析 XFF 则限流按 IP 分区形同虚设 ──
        //   安全红线:开了却不声明受信来源 = 采信任何人伪造的 X-Forwarded-For = 每个伪造 IP 开一个新分区 → 限流被完全绕过。
        //   宁可启动就炸,也不静默留一个可绕过的限流器(同 JwtKeyResolver 生产缺密钥的 fail-fast 成法)。
        var fwd = options.Api.ForwardedHeaders;
        if (fwd.Enabled)
        {
            if (fwd.KnownProxies.Length == 0 && fwd.KnownNetworks.Length == 0)
                throw new InvalidOperationException(
                    "已启用 TenonAdmin:Api:ForwardedHeaders,但未声明任何受信来源。请配置 KnownProxies(代理 IP)" +
                    "或 KnownNetworks(CIDR 网段,容器编排下更实际,如 172.16.0.0/12)。" +
                    "无条件采信 X-Forwarded-For 会让攻击者用伪造 IP 绕过限流并污染审计日志。");

            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                                   | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
                o.ForwardLimit = fwd.ForwardLimit;
                // 框架默认信回环(::1/127.0.0.1);容器里代理不是回环 → 清空后只信用户显式声明的来源。
                o.KnownProxies.Clear();
                o.KnownIPNetworks.Clear();
                foreach (var ip in fwd.KnownProxies) o.KnownProxies.Add(IPAddress.Parse(ip));
                foreach (var cidr in fwd.KnownNetworks) o.KnownIPNetworks.Add(IPNetwork.Parse(cidr));
            });
        }

        // ── 限流(§12/§14):按客户端 IP 固定窗口,认证端点(/api/v1/auth/*)更严;经上面的 IStartupFilter 挂中间件 ──
        //   计数走 ICacheProvider.IncrementAsync ⇒ 装 Redis 即跨副本共享(否则 N 个副本 = N × 阈值,爆破基线被静默削半)。
        //   实现与取舍见 RateLimitMiddleware 的类注释(为何不用 ASP.NET 的 PartitionedRateLimiter)。
        services.TryAddSingleton<RuntimeRateLimit>();
        services.AddHostedService(sp => sp.GetRequiredService<RuntimeRateLimit>());

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
