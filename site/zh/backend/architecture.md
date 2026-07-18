# 架构分层与包依赖

TenonAdmin 由六个 NuGet 包组成，其中构成核心链条的五个包依赖只能自上而下。这个次序不是分类习惯，而是承重约束：上层能引下层，下层永远看不见上层。第六个包 `TenonAdmin.Caching.Redis` 是挂在 `Core` 旁边的一条可选支线，不在这条主链之内，下文单说。

## 核心链条：五个包

```text
TenonAdmin.Core        纯契约:接口(I*Provider、I*Service)、Options、Result<T>、ErrorCode、AdminException。
   ↑                   无 SqlSugar、无 ASP.NET。
TenonAdmin.SqlSugar    数据层:ISqlSugarClient 单例(SqlSugarScope)、IRepository<>、实体基类、
   ↑                   CodeFirst DatabaseInitializer、种子运行器。
TenonAdmin.Services    领域层:实体(Sys*)、*Service 实现、RBAC / 数据范围提供者、事件总线。
   ↑                   实体定义在这一层,不在 SqlSugar 层。
TenonAdmin.AspNetCore  宿主集成:AddTenonAdmin / MapTenonAdmin、JWT、[RolePermission] / [ActiveSession]
                       过滤器、内置控制器、信封 / 异常 / 操作日志过滤器。

TenonAdmin             元包:只引用 AspNetCore。消费方装这一个,即传递引入整条栈。
```

旁支的 `TenonAdmin.Caching.Redis` 只依赖 `Core`，而 Core/SqlSugar/Services/AspNetCore 都不会反过来引用它：

```text
TenonAdmin.Caching.Redis   可选包:RedisCacheProvider(基于 StackExchange.Redis 的 ICacheProvider 实现),
                            消费方在 AddTenonAdmin() *之前* 调用 AddTenonAdminRedisCache(configuration) 即可启用。
   ↑
TenonAdmin.Core
```

各层职责与依赖方向：

| 包 | 职责 | 依赖 | 第三方运行时依赖 |
| --- | --- | --- | --- |
| `TenonAdmin.Core` | 契约、Options、`Result<T>`、`ErrorCode`、`AdminException`、`IIdGenerator` | 无 | 仅 Microsoft.* |
| `TenonAdmin.SqlSugar` | `SqlSugarScope` 单例、`IRepository<>`、`BaseEntity`/`DataEntity`、CodeFirst、种子 | Core | SqlSugarCore |
| `TenonAdmin.Services` | `Sys*` 实体、服务实现、RBAC、数据范围、事件总线 | SqlSugar、Core | SqlSugarCore |
| `TenonAdmin.AspNetCore` | JWT、授权过滤器、内置控制器、全局过滤器、`AddTenonAdmin` | Services、SqlSugar、Core | Microsoft.AspNetCore.* |
| `TenonAdmin`（元包） | 聚合入口 | AspNetCore |——|
| `TenonAdmin.Caching.Redis`（可选） | `RedisCacheProvider`：Redis 版 `ICacheProvider` | 仅 Core | StackExchange.Redis |

`TenonAdmin.Caching.Redis` 没有引入新机制，它就是上面那套 `TryAdd` 可替换性套用在缓存提供者上。消费方在 `AddTenonAdmin()` 之前调用 `AddTenonAdminRedisCache(configuration)`，内部用 `TryAddSingleton` 注册 `RedisCacheProvider`，抢先赢下注册，替换掉内核默认的进程内 `MemoryCacheProvider`。不调用这个方法，或没把 `TenonAdmin:Cache:Provider` 配成 `Redis`，内核的进程内默认实现照常工作，不受影响。

::: tip 实体住在 Services，不在 SqlSugar
数据层只提供 `IRepository<>` 和实体基类，具体的 `Sys*` 业务实体定义在 `TenonAdmin.Services`。原因是依赖方向：实体需要引用领域概念，而数据层不能反过来依赖领域层。
:::

::: warning 运行时依赖红线
核心包的第三方运行时依赖只有 SqlSugarCore + Microsoft.*。日志、雪花 ID 这些通常靠三方库（Serilog、Yitter.IdGenerator）的能力，内核都自带了单文件实现（`FileLoggerProvider`、`SnowflakeIdGenerator`），就是为了守住这条线。
:::

## 每层一个 `*Setup.cs`

每层的 DI 装配是一个静态扩展方法，命名一一对应：

- `SqlSugarSetup.AddTenonAdminSqlSugar()`：`backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs`
- `ServicesSetup.AddTenonAdminServices()`：`backend/src/TenonAdmin.Services/ServicesSetup.cs`
- `TenonAdminSetup.AddTenonAdmin()`：`backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs`

`AddTenonAdmin` 是组合根：它先绑定配置，再逐层向下调用。消费方看到的只有它。

```csharp
// backend/samples/MinimalHost/Program.cs —— 三行零配置起全站
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

## 组合根如何逐层向下

`AddTenonAdmin` 的装配次序（见 `TenonAdminSetup.cs`）:

1. **绑定配置**。`configuration.GetSection("TenonAdmin").Bind(options)`，再执行可选的 `configure` 回调覆写，最后把 `TenonAdminOptions` 及其各子节（`Database` / `Cache` / `Jwt` / `Security` / `Upload` / `Api` / `Id` / `Logging`）作为单例入容器。缺省即默认值，所以零配置可跑。
2. **雪花机器号校验**。选了 Redis 缓存（多实例意图）却没显式给 `TenonAdmin:Id:WorkerId` 时，启动即抛，借此把一个静默的主键冲突换成一条可读的启动错误。
3. **当前用户 + 数据范围环境**。HTTP 侧实现 `HttpContextCurrentUser`、`HttpContextDataScopeContext` 在此先 `TryAdd` 注册，压过 SqlSugar 层的 `AsyncLocal` 兜底实现。
4. **调用下层**。`AddTenonAdminSqlSugar(options.Database, entityAssemblies)` 装数据层，`AddTenonAdminServices()` 装领域服务。
5. **宿主集成**。JWT 密钥解析、认证/授权、MVC 控制器 + 全局过滤器、CORS、限流、OpenAPI、健康检查。

```csharp
// TenonAdminSetup.AddTenonAdmin 内,向下装配数据层与领域层
var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
entityAssemblies.AddRange(options.ApplicationAssemblies);
services.AddTenonAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()]);
services.AddTenonAdminServices();
```

顺带一提，每层都能独立装配：`AddTenonAdminSqlSugar` 是公开入口，允许在裸容器上单独调用（测试、以及只要数据层的消费方就这么用）。因此它内部对可选依赖用 `GetService` 而非 `GetRequiredService`。没有日志工厂就静默不打，不会凭空多出一个必需依赖导致起不来。

## 消费方的实体和控制器如何挂进来

消费方的业务程序集经 `options.ApplicationAssemblies` 登记（代码侧设置，不从配置绑定）:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyBusinessModule).Assembly);
});
```

登记后，这个程序集在组合根里走两条路：

- **实体参与 CodeFirst 建表**。组合根把内置 Services 程序集和消费方程序集合并成实体扫描源传给 `AddTenonAdminSqlSugar`，消费方实体因此一并被 `DatabaseInitializer` 建表。
- **控制器挂入同一 MVC 管道**。组合根对每个消费方程序集做 `mvc.AddApplicationPart(assembly)`，消费方控制器与内置控制器走同一套过滤器（异常信封、操作日志、裸返回包装）、同一套认证授权。

```csharp
// 控制器:内置 + 消费方,同一 MVC 管道
var mvc = services.AddControllers(o => { /* 全局过滤器 */ })
    .AddApplicationPart(typeof(TenonAdminSetup).Assembly);   // 内置控制器
foreach (var assembly in options.ApplicationAssemblies.Distinct())
    mvc.AddApplicationPart(assembly);                        // 消费方控制器
```

::: warning 改这条路径要当心
在 `TenonAdminSetup` 里动实体扫描或控制器注册时，务必保住这两条挂载路径。一旦漏掉，消费方模块会静默失效：表建不出来，控制器 404，而且不报错。
:::

## 元包只是一个聚合入口

`TenonAdmin.csproj` 本身没有代码，只有一条 `ProjectReference` 指向 `TenonAdmin.AspNetCore`。消费方装元包一个，即通过依赖传递拉起 AspNetCore → Services → SqlSugar → Core 整条栈。要更细粒度控制（比如只要数据层），也可以直接装下层包。
