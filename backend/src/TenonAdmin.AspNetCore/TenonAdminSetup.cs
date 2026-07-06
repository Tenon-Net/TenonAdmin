using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.AspNetCore;

/// <summary>用户侧入口:AddTenonAdmin() / MapTenonAdmin()(设计 §3.1)</summary>
public static class TenonAdminSetup
{
    public static IServiceCollection AddTenonAdmin(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TenonAdminOptions>? configure = null)
    {
        var options = new TenonAdminOptions();
        configuration.GetSection("TenonAdmin").Bind(options);   // 缺省则用默认值(零配置可跑)
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(options.Database);
        // 数据层(含首启建表 + 种子的 HostedService);登记 Services 层实体程序集供 CodeFirst 扫描
        services.AddTenonAdminSqlSugar(options.Database, [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();                       // 领域服务(显式 TryAdd,§5.7 注册模型)
        return services;
    }

    public static IEndpointRouteBuilder MapTenonAdmin(this IEndpointRouteBuilder endpoints)
    {
        // ponytail: 骨架用极简 /health;正式版换 Microsoft.Extensions.Diagnostics.HealthChecks(设计 §12)
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok", app = "TenonAdmin" }));
        return endpoints;
    }
}
