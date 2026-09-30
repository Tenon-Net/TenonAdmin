using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.AspNetCore;
using TenonAdmin.Integration;
using TenonAdmin.IntegrationTestHost;

// 第三方接入测试宿主 = 一个「消费者」:启用模块,自带业务实体、服务、自定义范围策略与开放端点。
var builder = WebApplication.CreateBuilder(args);
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IOpenApiDataScopePolicy, PartnerScopePolicy>());
builder.Services.AddTenonAdminIntegration(builder.Configuration);
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.UseIntegration();
    options.ApplicationAssemblies.Add(typeof(IntegrationProgram).Assembly);   // 消费者实体建表、控制器挂载
});
builder.Services.TryAddScoped<IDemoTicketService, DemoTicketService>();
builder.Services.TryAddScoped<DemoPartnerClient>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, DemoPartnerDeliveryAdapter>());
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, DemoPartnerDedupeOnlyAdapter>());
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, DemoPartnerNoDedupeAdapter>());
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, DemoPartnerQueryOnlyAdapter>());

var app = builder.Build();
app.MapTenonAdmin();
app.Run();

/// <summary>测试工厂定位本宿主程序集的标记类型(测试工程以 alias <c>integrationhost</c> 引用)。</summary>
public partial class IntegrationProgram { }
