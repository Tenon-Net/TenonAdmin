using TenonAdmin.AspNetCore;
using TenonAdmin.Caching.Redis;

// 验收基准(设计 §3.1):去掉那行可选包,就是三行、零配置即跑。
var builder = WebApplication.CreateBuilder(args);

// 可选包 TenonAdmin.Caching.Redis。必须在 AddTenonAdmin **之前**调用才赢 TryAdd(设计 §5.2)。
// 不配 TenonAdmin:Cache:Provider=Redis 时它是**空操作** —— 零配置体验不变,仍走进程内缓存。
// 多副本下它不是可选优化而是**前置条件**:进程内缓存意味着副本 A 的强退/权限失效传不到副本 B,
// 而会话缓存的 TTL 是刷新令牌寿命(天级)——强制下线会在半数请求上失效好几天。
builder.Services.AddTenonAdminRedisCache(builder.Configuration);

builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
