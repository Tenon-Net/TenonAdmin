using TenonAdmin.AspNetCore;
using TenonAdmin.Integration;
using TenonAdmin.Samples.Integration;

// 第三方接入消费者示例「合作方工单」。先启动本地可控第三方:
//   dotnet run --project backend/samples/IntegrationMockPartner          (http://127.0.0.1:5300)
// 再启动本示例(秘密经环境变量、用户机密或 appsettings.Development.json 提供,见 appsettings.Development.json.example):
//   dotnet run --project backend/samples/IntegrationSample
var builder = WebApplication.CreateBuilder(args);

// 启用第三方接入模块(TryAdd 注册;可在它之前注册同接口替换任一内置服务)
builder.Services.AddTenonAdminIntegration(builder.Configuration);
// 示例业务:服务、普通调用客户端、投递适配器、自定义范围策略
builder.Services.AddPartnerTicketSample();

builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.UseIntegration();                                          // 模块实体建表、管理与开放端点挂载
    o.ApplicationAssemblies.Add(typeof(SampleSetup).Assembly);   // 示例自己的实体建表、控制器挂载
});

var app = builder.Build();
app.MapTenonAdmin();
app.Run();
