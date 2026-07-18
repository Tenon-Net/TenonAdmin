# 项目结构与启动

这一页帮你把 `backend/` 的地形摸清：解决方案怎么分文件夹、`src/` 下六个包各自管什么、示例宿主怎么跑起来、测试套件怎么同时压"裸内核"和"带业务模块的消费方"两条路。至于依赖方向、可替换性、请求管道这些设计上的"为什么"，在[架构](/zh/backend/architecture)一页展开。

## 解决方案结构

`backend/TenonAdmin.slnx` 把所有项目分成三个解决方案文件夹：

| 文件夹 | 内容 |
| --- | --- |
| `samples/` | `MinimalHost`——零配置示例宿主，用于本地开发与手工验证 |
| `src/` | 六个正式发版的包 |
| `tests/` | `TenonAdmin.Tests`（测试套件）与 `TenonAdmin.TestHost`（一个最小化的消费方宿主） |

`src/` 下的包在[架构](/zh/backend/architecture)页有详细展开，这里只做定位：

| 包 | 一句话定位 |
| --- | --- |
| `TenonAdmin.Core` | 核心契约，零运行时依赖：`Result<T>`、`ErrorCode`、雪花 ID、安全与扩展点接口 |
| `TenonAdmin.SqlSugar` | 数据层：单一 SqlSugar 实例、CodeFirst 建表、幂等种子、审计/软删/数据范围全局过滤器、开放泛型仓储 |
| `TenonAdmin.Services` | 领域服务：认证 / RBAC / 机构 / 数据范围 / 字典 / 配置 / 日志 / 上传等业务服务与实体 |
| `TenonAdmin.AspNetCore` | 宿主集成：一键装配的 `AddTenonAdmin`/`MapTenonAdmin`、JWT 认证、`[RolePermission]` 授权、内置控制器与过滤器 |
| `TenonAdmin` | 元包：装这一个即拉起整条内核（AspNetCore + Services + SqlSugar + Core） |
| `TenonAdmin.Caching.Redis` | 可选包：基于 `StackExchange.Redis` 的 `ICacheProvider` 实现，在 `AddTenonAdmin()` 之前调用即启用 |

## 中心化包版本管理

`Directory.Packages.props` 开启了 `ManagePackageVersionsCentrally=true`——各 `.csproj` 只写包名（`<PackageReference Include="..." />`），版本号统一在这一份文件里锁定。其中几处版本号的注释直接写明了 CVE 缘由，而不只是跟随上游：

- `SQLitePCLRaw.bundle_e_sqlite3` 显式抬到 `3.0.3`:Microsoft.Data.Sqlite 传递依赖的 2.1.10/2.1.11 命中 SQLite 的一个 CVE(NU1903 GHSA-2m69-gcr7-jv3q),3.0.x 起已修补。
- `Microsoft.OpenApi` 显式抬到 `2.7.5`:`Microsoft.AspNetCore.OpenApi` 10.0.9 传递依赖的 2.0.0 命中一个高危 CVE（NU1903 GHSA-v5pm-xwqc-g5wc，影响范围 2.0.0-preview.11 至 2.7.4）,2.7.5 起修补。
- `Microsoft.Extensions.DependencyInjection.Abstractions` 抬到 `10.0.5`:`StackExchange.Redis` 3.0.11 传递依赖的 `Logging.Abstractions` 10.0.5 要求 `DI.Abstractions` ≥10.0.5，不抬版本的话集中管理的 10.0.0 会跟它冲突，报 NU1605 降级错误。

`Directory.Build.props` 为所有项目统一设置构建与包元数据：

- `TargetFramework` 是 `net10.0`,`Nullable` 与 `ImplicitUsings` 都已开启。
- `GenerateDocumentationFile` 开着，同时用 `NoWarn` 压掉 `CS1591`——发布的包带 XML 注释（消费方要能步进内核源码看懂在改哪一步，这是包价值的一部分），但不强制每个 public 成员都写注释，免得警告刷屏。
- NuGet 元数据也统一在这里：`Version`（`0.1.1`，发版时经 `-p:Version` 由 tag 覆盖）、`PackageLicenseExpression`(`Apache-2.0`)、`PackageTags`(`admin;rbac;sqlsugar;aspnetcore;scaffold;kernel`)。
- SourceLink 靠 `PublishRepositoryUrl`/`EmbedUntrackedSources`/`IncludeSymbols`（符号包用 `snupkg` 格式）接好，消费方调试时能直接步进内核源码。`ContinuousIntegrationBuild` 只在 `GITHUB_ACTIONS` 环境变量存在时才开——它会把内嵌源码路径规范化，本地开着反而会打乱调试路径。

::: tip `IsPackable` 默认是 false
`Directory.Build.props` 把 `IsPackable` 默认设为 `false`,`src/` 下每个正式包再显式打开。示例与测试项目沿用默认值，永远不会被打包。
:::

## 示例宿主

`backend/samples/MinimalHost/Program.cs` 是消费方真正会照抄的启动代码：

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdminRedisCache(builder.Configuration);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

`AddTenonAdminRedisCache` 也在这里被调用了，且排在 `AddTenonAdmin()` 之前——但只要配置里没把 `TenonAdmin:Cache:Provider` 设成 `Redis`，它就是空操作，不影响零配置体验（SQLite + 进程内缓存）。去掉这一行，就回到三行、真正零配置的基线。

它的 `appsettings.json` 默认关闭文件日志（`TenonAdmin:Logging:File:Enabled: false`——诊断信息靠 stdout 采集），标准 ASP.NET Core 日志级别照常设置。`appsettings.Development.json.example` 是被 gitignore 的 `appsettings.Development.json` 的模板，只放了种子超管账号名和一个空密码（留空是为了让内核首启时打印一个随机密码）。`Properties/launchSettings.json` 把开发环境地址锁定在 `http://localhost:5100`，并设置 `ASPNETCORE_ENVIRONMENT=Development`。

## 测试基础设施

`backend/tests/` 下有两个性质不同的项目：

- **`TenonAdmin.TestHost`** 是一个最小化的*消费方*宿主，不属于自动化测试套件本身——它把自己登记进 `options.ApplicationAssemblies.Add(typeof(Program).Assembly)`，让自己的实体、种子数据、控制器（`SampleWidget`、`SampleDoc`、`CustomDictController`）走一遍消费方挂载路径，供基于 `WebApplicationFactory<Program>` 的测试驱动。它演示的内容详见[搭建业务模块](/zh/guide/business-module)。
- **`TenonAdmin.Tests`** 才是真正的 xUnit 测试套件，靠 `dotnet test` 跑。

多数据库矩阵机制在 `TenonAdmin.Tests/TestDb.cs` 里。它读 `TENON_TEST_DBTYPE`（`MySql` / `SqlServer` / `PostgreSQL`，不设则默认走 SQLite），以及各数据库对应的连接串环境变量（`TENON_TEST_MYSQL`、`TENON_TEST_SQLSERVER`、`TENON_TEST_POSTGRESQL`）。对非 SQLite 的引擎，每次测试运行的库名由一个 `identity` 字符串经 SHA-256 哈希确定性派生（`tenon_it_` 前缀 + 哈希前 16 位十六进制），同一个 identity 永远对应同一个库——这支持"对同一个库二次启动"这类幂等测试用例。因为 SqlSugar 的 CodeFirst 只建表、不建库，`TestDb` 在 SqlSugar 接手之前，自己经原始的 `MySqlConnection`/`SqlConnection`/`NpgsqlConnection` 直连服务器完成建库和删库。

## 配置节总览

一切都从 `appsettings.json` 的 `TenonAdmin` 节绑定进 `TenonAdminOptions`(`backend/src/TenonAdmin.Core/Options/TenonAdminOptions.cs`):

| 属性 | 子配置类型 | 默认值示例 |
| --- | --- | --- |
| `Database` | `AdminDatabaseOptions` | `DbType = "Sqlite"`、`ConnectionString = "Data Source=./data/admin.db"`、`EnableCodeFirst = true` |
| `Cache` | `AdminCacheOptions` | `Provider = "Memory"`、`KeyPrefix = "tenon:"`、`PermissionMinutes = 20` |
| `Seed` | `AdminSeedOptions` | 超管账号/密码种子 |
| `Jwt` | `AdminJwtOptions` | 签名密钥/签发者/有效期 |
| `Security` | `AdminSecurityOptions` | 会话并发策略 |
| `Upload` | `AdminUploadOptions` | 存储根目录、大小上限、后缀白名单 |
| `Api` | `AdminApiOptions` | 禁用模块列表 |
| `DemoMode` | `bool` | `false`——为 `true` 时仅放行 GET/HEAD/OPTIONS，其余写请求一律以错误码 `41002` 拒绝 |
| `Id` | `AdminIdOptions` | `WorkerId`——默认为 `null`（回落为 0）;多实例水平扩展时必须为每个实例显式配置 |
| `Logging` | `AdminLoggingOptions` | 文件日志诊断，默认关闭 |

`ApplicationAssemblies` 是个例外——它是代码侧设置的 `List<Assembly>`（如上面示例宿主与 `TestHost` 的代码片段所示），不从配置绑定，因为程序集引用没法从 JSON 里来。

摸清了结构，下一步自然是看这几个包如何装配到一起——[架构分层与包依赖](/zh/backend/architecture)从依赖方向讲起;构建、测试的 CLI 命令则在[贡献指南](/zh/community/contributing)里。
