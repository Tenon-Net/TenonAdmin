using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 独立 Worker 进程的组合根(scheduling-ledger §10.2)——纯 Generic Host、无 ASP.NET 的装配路径。
/// <para><b>绝大多数消费者用不到这个</b>:默认形态是 <c>AddTenonAdmin()</c> 三行,调度器就跑在 API 进程内,
/// 多副本靠 DB 选主自动互备。只有想要「API 停了任务照跑」或把任务负载隔离出 API 进程时,才另起一个 Worker 进程
/// (照抄 <c>samples/WorkerHost</c>,Program.cs 也是三行)。</para>
/// <para>为什么需要它:选项 POCO 的 <c>AddSingleton</c> 目前全内联在 AspNetCore 层的 <c>TenonAdminSetup</c> 里,
/// 纯 Services 宿主无处复用——本方法就是那段装配的 Worker 版。</para>
/// </summary>
public static class WorkerSetup
{
    /// <summary>
    /// 装配一个只跑调度器的 Worker:绑定 <c>TenonAdmin</c> 配置节 → 注册 Services 层依赖的各选项 POCO →
    /// 数据层 + 领域服务(其中就包含 <see cref="JobSchedulerService"/> 的托管注册)。
    /// </summary>
    /// <param name="services">宿主的服务集合</param>
    /// <param name="configuration">宿主配置(读 <c>TenonAdmin</c> 节)</param>
    /// <param name="configure">代码侧覆写(在绑定之后、注册之前生效)</param>
    /// <exception cref="InvalidOperationException">
    /// 未显式配置 <c>TenonAdmin:Id:WorkerId</c> 时直接抛——Worker 天然是多实例形态(至少与一个 API 副本同时在跑),
    /// 雪花机器号同号会让不同进程在同毫秒发出相同 Id、撞主键。这比 API 侧的 Redis 守卫更严:API 可能真是单实例,Worker 不可能。
    /// </exception>
    public static IServiceCollection AddTenonAdminWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TenonAdminOptions>? configure = null)
    {
        var options = new TenonAdminOptions();
        configuration.GetSection("TenonAdmin").Bind(options);
        configure?.Invoke(options);

        if (options.Id.WorkerId is null)
            throw new InvalidOperationException(
                "Worker 进程必须显式配置 TenonAdmin:Id:WorkerId(0–63,与所有 API 副本及其它 Worker 互不相同)。" +
                "Worker 天然是多实例形态,机器号同号会让不同进程在同毫秒发出相同的雪花 Id、撞主键。");
        // 与 AddTenonAdmin 共用:租约、正数项、HTTP 围栏 CIDR——Worker 才是真正执行任务的一侧,不能漏
        AdminJobsOptionsValidation.Validate(options.Jobs);

        services.AddSingleton(options);
        services.AddSingleton(options.Database);
        services.AddSingleton(options.Cache);
        services.AddSingleton(options.Id);
        services.AddSingleton(options.Jobs);
        services.AddSingleton(options.Email);      // 连败告警要发邮件
        services.AddSingleton(options.Security);   // 安全策略读取层的兜底值
        services.AddSingleton(options.Seed);
        services.AddSingleton(options.Upload);     // FileGcService 是 Services 层的托管服务,构造要它
        services.AddSingleton(options.Logging);

        services.AddTenonAdminSqlSugar(options.Database, options.ApplicationAssemblies, options.AdditionalDatabases);
        services.AddTenonAdminServices();
        return services;
    }
}
