using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.AspNetCore;
#if (integration)
using TenonAdmin.Integration;
using TenonApp.Integrations;
#endif
using TenonApp.Modules;

// TenonAdmin 消费方 host。首次启动控制台会打印随机超管密码,用它登录。
// 加业务模块:复制 Modules/SampleDoc* 四件套改名,并在下方追加一行 TryAddScoped。
var builder = WebApplication.CreateBuilder(args);

#if (integration)
// 第三方接入模块(TryAdd 注册;要替换其中任一内置服务,在这一行之前注册同一接口)。
builder.Services.AddTenonAdminIntegration(builder.Configuration);

#endif
// 注册内核,并把本程序集登记为业务程序集:其中的 [SugarTable] 实体自动建表、[ApiController] 控制器自动挂路由。
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);
#if (integration)
    o.UseIntegration();   // 第三方接入的表、管理与开放端点、菜单和后台任务
#endif
});

// 业务服务:内核内置服务用 TryAdd 可被覆盖;你自己的服务在此显式登记(每个模块一行)。
builder.Services.TryAddScoped<ISampleDocService, SampleDocService>();
#if (integration)

// 第三方接入示例(见 Integrations/README.md):普通调用客户端、事务内入队的业务服务、投递适配器。
builder.Services.TryAddScoped<PartnerClient>();
builder.Services.TryAddScoped<ISampleDocSyncService, SampleDocSyncService>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAdapter, SampleDocSyncAdapter>());
#endif

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
