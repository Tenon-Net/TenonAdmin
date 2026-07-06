using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 领域服务装配(设计 §2.2)。框架内置服务在此<b>显式 TryAdd</b>——不靠扫描,可预测、
/// 可被用户前置注册整体替换(设计 §5.7 注册模型:内置显式、用户扫描)。
/// </summary>
public static class ServicesSetup
{
    public static IServiceCollection AddTenonAdminServices(this IServiceCollection services)
    {
        // 密码哈希:PBKDF2 默认实现,无状态 → Singleton;用户可前置注册替换(§5.2)
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // 认证:模板方法样板服务(§5.3);Scoped——按请求生命周期,与仓储一致
        services.TryAddScoped<IAuthService, AuthService>();

        // 种子:多实现集合,TryAddEnumerable 按实现类型防重
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SuperAdminSeed>());

        return services;
    }
}
