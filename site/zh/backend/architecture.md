# 架构分层与包依赖

大多数项目只需要安装 `TenonAdmin` 元包，再调用 `AddTenonAdmin()`。只有准备单独使用数据层、接入可选能力，或者排查依赖冲突时，才需要关心各个包如何分层。五个核心包只向下依赖；Redis、Excel 和四个登录包只依赖 `Core`，工作流包则依赖 `AspNetCore`。元包不会自动引入这些可选能力。

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

六个轻量扩展包只依赖 `Core`，核心链不会反向引用它们：

```text
TenonAdmin.Caching.Redis   可选包:RedisCacheProvider(基于 StackExchange.Redis 的 ICacheProvider 实现),
                            消费方在 AddTenonAdmin() *之前* 调用 AddTenonAdminRedisCache(configuration) 即可启用。
TenonAdmin.Auth.WeCom      可选包:企业微信扫码登录的 IExternalAuthProvider 实现。
TenonAdmin.Auth.DingTalk   可选包:钉钉扫码登录的 IExternalAuthProvider 实现。
TenonAdmin.Auth.GitHub     可选包:GitHub OAuth App 登录的 IExternalAuthProvider 实现。
TenonAdmin.Auth.WeChat     可选包:微信开放平台登录的 IExternalAuthProvider 实现。
TenonAdmin.Excel           可选包:xlsx 读写与带下拉的模板生成,消费方在 AddTenonAdmin() *之前*
                            调用 AddTenonAdminExcel() 即可启用。
   ↑
TenonAdmin.Core
```

四个登录可选包只引 `Core` 和 Microsoft.*，没有厂商 SDK。`TenonAdmin.Workflow` 是另一类可选包：它需要复用控制器和请求管线，因此引用 `TenonAdmin.AspNetCore`，安装后调用 `AddTenonAdminWorkflow()` 与 `UseWorkflow()` 才启用。

各层职责与依赖方向：

| 包 | 职责 | 依赖 | 第三方运行时依赖 |
| --- | --- | --- | --- |
| `TenonAdmin.Core` | 契约、Options、`Result<T>`、`ErrorCode`、`AdminException`、`IIdGenerator` | 无 | 仅 Microsoft.* |
| `TenonAdmin.SqlSugar` | `SqlSugarScope` 单例、`IRepository<>`、`BaseEntity`/`DataEntity`、CodeFirst、种子 | Core | SqlSugarCore |
| `TenonAdmin.Services` | `Sys*` 实体、服务实现、RBAC、数据范围、[事件总线](/zh/backend/event-bus) | SqlSugar、Core | SqlSugarCore |
| `TenonAdmin.AspNetCore` | JWT、授权过滤器、内置控制器、全局过滤器、`AddTenonAdmin` | Services、SqlSugar、Core | Microsoft.AspNetCore.* |
| `TenonAdmin`（元包） | 聚合入口 | AspNetCore |——|
| `TenonAdmin.Caching.Redis`（可选） | `RedisCacheProvider`：Redis 版 `ICacheProvider` | 仅 Core | StackExchange.Redis |
| `TenonAdmin.Auth.WeCom`（可选） | 企业微信扫码登录的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `TenonAdmin.Auth.DingTalk`（可选） | 钉钉扫码登录的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `TenonAdmin.Auth.GitHub`（可选） | GitHub OAuth App 登录的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `TenonAdmin.Auth.WeChat`（可选） | 微信开放平台登录的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `TenonAdmin.Excel`（可选） | xlsx 读写与模板生成的 `IExcelReader`/`IExcelWriter`/`IExcelTemplateBuilder` | 仅 Core | MiniExcel、DocumentFormat.OpenXml |
| `TenonAdmin.Workflow`（可选） | 审批定义、引擎、待办与设计器 API | AspNetCore | Microsoft.AspNetCore.* |
| `TenonAdmin.Integration`（可选） | 应用凭据、开放接口、出站调用与可靠投递 | AspNetCore | Microsoft.AspNetCore.* |

系统集成使用独立的 `TenonAdmin.Integration` 可选包，依赖 `AspNetCore`，不由元包自动引入。安装包含该模块的发行版本后，在 `AddTenonAdmin` 前调用 `AddTenonAdminIntegration()`，并在内核的配置回调中调用 `UseIntegration()`。应用、凭据和发送任务的操作见[系统集成](/zh/guide/integration)。

## 按使用场景选入口

- **开发完整管理后台**：安装 `TenonAdmin`。它会传递引入完整核心链条，也是快速开始和业务模块示例采用的方式。
- **只要数据访问能力**：直接安装 `TenonAdmin.SqlSugar`，调用 `AddTenonAdminSqlSugar()`。此时不会注册 JWT、控制器和宿主过滤器。
- **接入 Redis、外部登录或 Excel**：保留元包，再安装对应可选包，并调用它的注册方法。依赖 `TryAdd` 接管内置接口的注册应放在 `AddTenonAdmin()` 之前。
- **启用工作流**：额外安装 `TenonAdmin.Workflow`，注册 `AddTenonAdminWorkflow()` 并在 `AddTenonAdmin` 的配置回调中调用 `UseWorkflow()`。元包不会默认启用它。

判断是否选对很直接：完整宿主应能解析内置控制器；只装数据层的容器应能解析 `ISqlSugarClient` 和 `IRepository<>`，却不会出现认证或 MVC 服务。

`TenonAdmin.Caching.Redis` 没有引入新机制，就是把内核那套 `TryAdd` 可替换性套用在缓存提供者上。消费方在 `AddTenonAdmin()` 之前调用 `AddTenonAdminRedisCache(configuration)`。它内部用 `TryAddSingleton` 注册 `RedisCacheProvider`，抢先赢下注册，替换掉内核默认的进程内 `MemoryCacheProvider`。不调用这个方法，或者没把 `TenonAdmin:Cache:Provider` 配成 `Redis`，内核的进程内默认实现照常工作，不受影响。

`TenonAdmin.Excel` 走同一条路：未安装可选包时，三个默认 codec 都会抛 `ErrorCode.ExcelProviderMissing`（`46001`）。安装包并在 `AddTenonAdmin()` 之前调用 `AddTenonAdminExcel()` 后，真实实现才会进入容器。未使用导入导出的项目不需要携带它的运行时依赖。接法见[给自己的实体接导入导出](/zh/guide/import-export)。

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

`AddTenonAdmin` 是完整宿主的组合根：它先绑定配置，再逐层向下调用。普通业务项目只需要这个入口，不必分别调用下层的 Setup。

```csharp
// backend/samples/MinimalHost/Program.cs —— 三行零配置起全站
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

## 组合根如何逐层向下

`AddTenonAdmin` 的装配次序（见 `TenonAdminSetup.cs`）：

1. **绑定配置**。先 `configuration.GetSection("TenonAdmin").Bind(options)`，再跑一遍可选的 `configure` 回调做覆写。最后把 `TenonAdminOptions` 及其各子节（`Database` / `Cache` / `Jwt` / `Security` / `Upload` / `Api` / `Id` / `Logging`）作为单例入容器。缺省即默认值，所以零配置可跑。
2. **雪花机器号**。未配 `TenonAdmin:Id:WorkerId` 时同机抢文件锁、共享库在 `sys_worker_lease` 领空闲槽。两个实例写成同一个号，后到的起不来。为什么同号会撞主键，[数据层与审计](./data-layer.md)里雪花 ID 的位运算讲得更细。
3. **当前用户 + 数据范围环境**。HTTP 侧实现 `HttpContextCurrentUser`、`HttpContextDataScopeContext` 在此先 `TryAdd` 注册，压过 SqlSugar 层的 `AsyncLocal` 兜底实现。
4. **调用下层**。`AddTenonAdminSqlSugar(options.Database, entityAssemblies, options.AdditionalDatabases)` 装数据层（主库 + 可选副库），`AddTenonAdminServices()` 装领域服务。
5. **宿主集成**。JWT 密钥解析、认证/授权、MVC 控制器 + 全局过滤器、CORS、限流、OpenAPI、健康检查。

```csharp
// TenonAdminSetup.AddTenonAdmin 内,向下装配数据层与领域层
var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
entityAssemblies.AddRange(options.ApplicationAssemblies);
services.AddTenonAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()], options.AdditionalDatabases);
services.AddTenonAdminServices();
```

每层都能独立装配。只需要数据访问时，可以在容器上直接调用 `AddTenonAdminSqlSugar`，不注册 JWT、控制器等宿主能力。数据层因此用 `GetService` 获取可选依赖；没有日志工厂时只是不输出日志，不会阻止数据层启动。

## 消费方的实体和控制器如何挂进来

消费方的业务程序集通过 `options.ApplicationAssemblies` 登记。这是代码侧设置，不从配置绑定：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyBusinessModule).Assembly);
});
```

登记后，这个程序集在组合根里走两条路：

- **实体参与 CodeFirst 建表**。组合根把内置 Services 程序集和消费方程序集合并成一份实体扫描源，传给 `AddTenonAdminSqlSugar`。消费方实体因此一并被 `DatabaseInitializer` 建表。
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

`TenonAdmin.csproj` 本身没有代码，只有一条 `ProjectReference` 指向 `TenonAdmin.AspNetCore`。消费方只装元包这一个，依赖传递就会拉起 AspNetCore → Services → SqlSugar → Core 整条栈。想要更细粒度的控制，比如只要数据层，直接装下层包也行。
