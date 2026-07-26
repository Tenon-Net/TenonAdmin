using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.TestHost;

// 集成测试宿主 = 一个"用户 App":除内置能力外,还带自己的业务模块(实体 + 种子 + 控制器)。
// 把本程序集登记为 ApplicationAssemblies(§5.7)——验证用户实体能 CodeFirst 建表、控制器能注册路由。
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration, o => o.ApplicationAssemblies.Add(typeof(Program).Assembly));
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SampleWidgetSeed>());   // 用户自定义种子
builder.Services.TryAddScoped<ISampleDocService, SampleDocService>();   // 示例机构隔离业务服务(DataEntity 范本)
builder.Services.TryAddScoped<SampleDocExportProfile>();                // 示例导出档案(消费方接导出的范本,excel-ledger §9 G5-12)
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, SampleJob>());   // 示例定时任务处理器(scheduling-ledger §6)
var app = builder.Build();
app.MapTenonAdmin();
app.Run();

// WebApplicationFactory<Program> 需要可见的入口点类型
public partial class Program { }
