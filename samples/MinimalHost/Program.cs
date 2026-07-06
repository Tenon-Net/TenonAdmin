using TenonAdmin.AspNetCore;

// 验收基准(设计 §3.1):就这三行,零配置即跑
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
