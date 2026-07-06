using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        services.AddSingleton(options.Jwt);
        services.TryAddSingleton(TimeProvider.System);          // 统一时间源(§12),测试可换 Fake

        // ── 数据层 + 领域服务(实体程序集在此登记,§5.7 注册模型)──────────────
        services.AddTenonAdminSqlSugar(options.Database, [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();

        // ── JWT:签名密钥惰性解析一次(含开发密钥持久化 + 警告),签发与验证共用同一实例 ──
        services.TryAddSingleton(sp =>
            JwtKeyResolver.Resolve(options.Jwt, sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtKeyResolver))));
        services.TryAddSingleton<ITokenProvider>(sp => new JwtTokenProvider(
            options.Jwt, sp.GetRequiredService<SymmetricSecurityKey>(), sp.GetRequiredService<TimeProvider>()));

        // 权限码提供者:RBAC 模块接入前为"空集合"占位——非超管默认全拒(§14 授权默认拒绝)
        services.TryAddSingleton<IPermissionProvider, DefaultPermissionProvider>();

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
                    NameClaimType = "unique_name",              // User.Identity.Name = 登录账号
                };
            });
        services.AddAuthorization();

        // ── MVC 控制器:本程序集作为 ApplicationPart 挂入宿主;业务异常过滤器全局生效 ──
        services.AddControllers(o => o.Filters.Add<AdminExceptionFilter>())
            .AddApplicationPart(typeof(TenonAdminSetup).Assembly);

        return services;
    }

    public static IEndpointRouteBuilder MapTenonAdmin(this IEndpointRouteBuilder endpoints)
    {
        // 内置控制器路由(认证、探针;后续模块的控制器自动包含)
        endpoints.MapControllers();

        // ponytail: 极简 /health;正式版换 Microsoft.Extensions.Diagnostics.HealthChecks(设计 §12)
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok", app = "TenonAdmin" }));
        return endpoints;
    }
}
