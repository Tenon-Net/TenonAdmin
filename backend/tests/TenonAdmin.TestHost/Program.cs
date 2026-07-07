using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.AspNetCore;
using TenonAdmin.SqlSugar;
using TenonAdmin.TestHost;

// 集成测试宿主 = 一个"用户 App":除内置能力外,还带自己的业务模块(实体 + 种子 + 控制器)。
// 把本程序集登记为 ApplicationAssemblies(§5.7)——验证用户实体能 CodeFirst 建表、控制器能注册路由。
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration, o => o.ApplicationAssemblies.Add(typeof(Program).Assembly));
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SampleWidgetSeed>());   // 用户自定义种子
var app = builder.Build();
app.MapTenonAdmin();
app.Run();

// WebApplicationFactory<Program> 需要可见的入口点类型
public partial class Program { }
