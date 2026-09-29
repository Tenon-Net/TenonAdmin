using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Integration;

namespace TenonAdmin.Samples.Integration;

/// <summary>示例业务的服务注册:业务服务、普通调用客户端、投递适配器与自定义范围策略。</summary>
public static class SampleSetup
{
    /// <summary>
    /// 在 <c>AddTenonAdminIntegration</c> 之前或之后调用均可(全部 <c>TryAdd</c>)。
    /// 自定义范围策略与适配器都是集合注册:同名适配器重复注册会在启动时被拒绝。
    /// </summary>
    public static IServiceCollection AddPartnerTicketSample(this IServiceCollection services)
    {
        services.TryAddScoped<IPartnerTicketService, PartnerTicketService>();
        services.TryAddScoped<PartnerDirectoryClient>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, PartnerTicketSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, LegacyTicketSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOpenApiDataScopePolicy, PartnerScopePolicy>());
        return services;
    }
}
