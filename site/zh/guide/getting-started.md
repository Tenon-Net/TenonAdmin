# 快速开始

你大概已经开了另一个窗口，准备先把数据库装上。不用装：克隆完仓库直接 `dotnet run`，控制台就打出一串超管密码，浏览器能登进去。SQLite 文件、表结构、种子数据全是内核首启自己长出来的，配置一行没写。

::: tip 前提条件
- .NET 10 SDK
- 想连前端一起跑，再装 Node.js（建议 20+）
:::

## 先把示例跑起来

仓库自带一个最小化示例宿主 `backend/samples/MinimalHost`，它的 `Program.cs` 只有几行接线。克隆仓库后直接运行：

```bash
dotnet run --project backend/samples/MinimalHost
```

首次启动会自动做三件事：用默认 SQLite 建表（CodeFirst，库文件落在 `backend/samples/MinimalHost/data/` 下），写入种子数据（菜单、角色、超级管理员账号），然后监听 `http://localhost:5100`（这个端口在 `launchSettings.json` 里写死，避开 macOS 上 AirPlay 占用的 5000）。

本地想一键起前后端，用仓库根的 `dev.bat`。它在两个窗口里分别拉起 MinimalHost 和前端 Vite（首次运行顺带装好前端依赖），`stop.bat` 停掉它们。

## 确认三个探针

服务起来后，先确认这三个端点都通：

```bash
# 存活探针,只看进程在不在,不碰任何依赖
curl http://localhost:5100/health

# 就绪探针:数据库 + 缓存都连通才返回 Healthy
curl http://localhost:5100/health/ready

# OpenAPI 契约,仅 Development 环境挂载,是前端 gen:api 的数据源
curl http://localhost:5100/openapi/v1.json
```

前两个应返回 `Healthy`。`/openapi/v1.json` 返回一大坨 JSON，后面生成前端类型要用它；这个端点生产环境不挂载，线上请求它得到 404 是预期行为，不是漏配。

## 登录，调第一个接口

`GET /api/v1/ping` 是内核里最小的受保护接口，带有效令牌才放行。登录之前，先弄清密码从哪来。

种子只在 `sys_user` 表为空时跑一次。跑 MinimalHost 属于零配置启动，没有显式配密码，内核就自己生成一个 16 位随机密码。随机源是加密安全的，`0/O`、`1/l/I` 这类易混淆字符已经剔掉。它只在**建号那一次**启动的控制台日志里打印，用一个边框圈出来，仅此一次：

```text
╔══════════════════════════════════════════════════════╗
║  TenonAdmin 首次启动,已创建超级管理员                  ║
║  账号: superAdmin
║  密码: xxxxxxxxxxxxxxxx
║  此密码仅本次显示,请登录后立即修改!                    ║
╚══════════════════════════════════════════════════════╝
```

账号固定是 `superAdmin`，把这串密码抄下来。

::: warning 随机密码只打印一次
没记下来也别慌：本地实验环境删掉 `backend/samples/MinimalHost/data` 下的数据库文件重新 `dotnet run`，空库会重新播种。生产库不能这么清。那边要么先配好固定密码（见下），要么登录后立刻改密。
:::

想要一个自己说了算的固定密码（团队共享、CI、反复删库重来），把 `backend/samples/MinimalHost/appsettings.Development.json.example` 拷成 `appsettings.Development.json`，填上 `Seed:AdminPassword`：

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "你的密码" } } }
```

这个文件被 `.gitignore` 排除（装的是本地凭证），不会进版本库。配了它启动日志就不再打印随机密码，直接用你设的账号密码登。注意种子只认空库：库里只要已有任意用户，改这里也不会覆盖已存在的账号，要重置只能删库重来。

默认没开图形验证码（`Security:Captcha:Enabled` 默认关），登录只要账号密码：

```bash
curl -X POST http://localhost:5100/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"account":"superAdmin","password":"<上面拿到的密码>"}'
```

响应信封里的 `data.accessToken` 就是令牌：

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": false } }
```

超管种子不强制首登改密，`mustChangePassword` 是 `false`（由管理员建号或被重置密码的普通用户才会是 `true`，前端据此强制跳改密页）。带上令牌调 ping:

```bash
curl http://localhost:5100/api/v1/ping \
  -H "Authorization: Bearer <accessToken>"
```

返回：

```json
{ "code": 0, "data": { "pong": true, "account": "superAdmin", "at": "2026-07-...T..." } }
```

不带令牌，或令牌过期/被吊销，拿到的是 `401`（标准信封，`code=40006`）。超管（令牌里的 `sadm` 声明）自动绕过后续的 `[RolePermission]` 权限码校验；普通用户要先在菜单管理里挂上对应路由、在角色管理里授权，才调得通同一个接口。这条链路的完整写法见[新建业务模块](/zh/guide/business-module)。

## 三行代码接进你自己的项目

上面跑的是仓库自带示例。真要把内核接进你自己的 ASP.NET Core 项目，先装元包：

```bash
dotnet add package TenonAdmin
```

当前版本是 `0.1.1`，已发布到 nuget.org。核心接入只有三行：

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` 绑配置、注册 JWT/RBAC/数据权限/日志等全部服务；`MapTenonAdmin` 挂路由、健康检查和（dev 下的）OpenAPI 文档。默认 SQLite、零配置即可跑。

要跨副本共享会话和缓存（多实例部署），额外装 `TenonAdmin.Caching.Redis`，并在 `AddTenonAdmin` **之前**调 `AddTenonAdminRedisCache(builder.Configuration)`。因为内核的可替换服务都用 `TryAdd` 注册，谁先注册谁赢，晚于 `AddTenonAdmin` 就抢不过内置的进程内缓存了。没配 `Cache:Provider=Redis` 时这行是空操作，单实例开发不受影响。

需要更细粒度的依赖控制，可以只引某一层（`.AspNetCore` / `.Services` / `.SqlSugar` / `.Core`）。这些包为什么这么分层、「可替换」到底怎么替，归[核心概念](/zh/guide/concepts)讲透；本页只管把它跑起来。

> 1.0 之前 API 仍可能调整，破坏性变更会在[更新日志](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)里明确标出。开发在 `dev` 分支进行。

## 顺手起前端

前端是仓库 `web/` 目录下的 Vue 3 + Naive UI 管理端模板：

```bash
cd web
npm install
npm run dev
```

Vite 起在 `http://localhost:5173`，内置反向代理把 `/api` 和 `/openapi` 原样转发到后端 `:5100`（可用环境变量 `TENON_API_TARGET` 覆盖目标），所以浏览器眼里只有一个源，本地开发不用配跨域。打开 `5173`，用同一个超管账号密码登录，就能看到完整后台。

前端要重新生成 API 类型（`npm run gen:api`）时后端必须在跑。它是从运行中的 `/openapi/v1.json` 抓契约，不是离线生成。

::: tip 想拿它当一次性脚手架（soybean / vite 那种）?
上面 `cd web` 是「克隆仓库、跟着上游升级」的路子，也是推荐路径：前端会和走 NuGet 升级的后端契约同步演化。如果你只想要一份拷贝、之后完全自己维护，用 degit 拉一份无 `.git` 历史的快照当起点：

```bash
npx degit Tenon-Net/TenonAdmin/web my-web
```

代价明确：**没有升级通道**。上游修了 bug 得自己读 diff 手动搬，且快照会与走 NuGet 升级的后端契约漂移。想持续吃上游修复，别走快照，走[同步 Fork 与上游](/zh/guide/sync-fork)。那套接缝就是为把这条路的合并冲突压到近乎为零而做的。
:::

## 换掉默认数据库

零配置默认用 SQLite（`Data Source=./data/admin.db`，相对 ContentRoot）。换正式数据库不用改代码，`TenonAdmin:Database` 一段配置说了算，改 `DbType` + `ConnectionString` 两项即可（`Sqlite` / `MySql` / `SqlServer` / `PostgreSQL` 都支持）：

```json
{
  "TenonAdmin": {
    "Database": {
      "DbType": "MySql",
      "ConnectionString": "Server=127.0.0.1;Port=3306;Database=tenon;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;"
    }
  }
}
```

容器化部署走环境变量更顺手（双下划线分层）：

```bash
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
```

::: warning 生产不会自动建表
`ASPNETCORE_ENVIRONMENT=Production` 时，即便开了 CodeFirst 也**不会**自动建表。这道安全闸门防的是线上误改表结构。空库首次上生产，要么临时打开 `EnableCodeFirstInProduction: true` 让它自己建一次，要么让 DBA 手工建。详见[部署指南](/zh/guide/deployment/)。
:::

内核跑通、库也换好之后，下一站是在它上面端到端加一个自己的业务模块，见[新建业务模块](/zh/guide/business-module)。
