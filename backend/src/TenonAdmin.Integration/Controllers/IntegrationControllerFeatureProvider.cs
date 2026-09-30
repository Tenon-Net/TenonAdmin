using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace TenonAdmin.Integration;

/// <summary>
/// 把模块的管理与开放控制器挂回控制器集合。它们都标了 <c>[NonController]</c>:Web SDK 宿主只要引用本包就会把本程序集
/// 自动加成应用部件,默认发现会因此跳过它们——只引用、未启用的宿主不暴露任何端点;
/// 由 <see cref="IntegrationSetup.AddTenonAdminIntegration"/> 注册本提供者后才出现。
/// </summary>
internal sealed class IntegrationControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private static readonly TypeInfo[] Controllers =
    [
        typeof(IntegrationAppController).GetTypeInfo(),
        typeof(IntegrationCatalogController).GetTypeInfo(),
        typeof(IntegrationDeliveryController).GetTypeInfo(),
        typeof(IntegrationInboundLogController).GetTypeInfo(),
        typeof(IntegrationOutboundLogController).GetTypeInfo(),
        typeof(OpenAppWhoAmIController).GetTypeInfo(),
    ];

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var controller in Controllers)
            if (!feature.Controllers.Contains(controller))
                feature.Controllers.Add(controller);
    }
}
