# 快速开始

首次体验可以直接运行仓库中的示例。后端默认使用 SQLite，并在首次启动时创建数据库、表和管理员账号，无需另外安装数据库服务。启动后端和一套前端后，就能在浏览器中登录后台。

需要 .NET 10 SDK 和 Git；运行前端建议使用 Node.js 22.12 或更高版本。前后端请使用配套版本，扩展包与 `TenonAdmin` 保持相同版本。正式发布记录见[更新日志](/zh/changelog)，`dev` 分支可能包含尚未发布的功能。

## 先把示例跑起来

在终端执行以下命令。后续后端命令都从仓库根目录运行：

```bash
git clone --branch dev --single-branch https://github.com/Tenon-Net/TenonAdmin.git
cd TenonAdmin
dotnet run --project backend/samples/MinimalHost
```

保持这个终端运行。看到 `http://localhost:5100` 的监听信息，表示后端已启动。SQLite 数据库保存在 `backend/samples/MinimalHost/data/` 下；表和初始菜单、角色由框架创建。

首次创建管理员账号时，控制台会显示账号 `superAdmin` 和随机密码。**密码只在创建账号的那次启动显示，请先保存。** 如果数据库已有用户，重启不会生成新密码，也不会覆盖已有账号。

## 启动前端并登录 {#顺手起前端}

另开一个终端，从仓库根目录选择一套前端。Vue 使用 Naive UI，React 使用 Ant Design；两套连接同一个后端，任选其一即可。

::: code-group

```bash [Vue (web/)]
cd web
npm install
npm run dev
```

```bash [React (web-react/)]
cd web-react
npm install
npm run dev
```

:::

Vue 打开 `http://localhost:5175`，React 打开 `http://localhost:5174`。使用 `superAdmin` 和刚保存的密码登录，登录后修改初始密码。能进入后台并打开菜单，说明前后端已连通。

登录后，从左侧菜单进入用户管理。下图中的用户是示例数据，首次启动时只需确认自己的管理员账号能够显示；筛选区和新增按钮是后续业务页面会复用的布局。

[![Vue 用户管理页面，点击查看原图](/screenshots/vue-admin.png)](/screenshots/vue-admin.png)

开发服务器会把 `/api` 和 `/openapi` 请求转发到后端 `:5100`，本地无需配置跨域。后端地址不同时，通过 `TENON_API_TARGET` 调整代理目标。若登录失败，先确认后端终端仍在运行，再核对密码来源；详细排查见[常见问题](/zh/faq)。

接下来，了解[核心概念](/zh/guide/concepts)，或直接[添加业务模块](/zh/guide/business-module)。如果要把模板作为自己项目的起点，参考[选择前端模板](/zh/guide/frontend-templates)和[同步上游](/zh/guide/sync-fork)。下面的接口调试与配置说明可在需要时查阅。

## 三行代码接进你自己的项目

上面跑的是仓库自带示例。真要把内核接进你自己的 ASP.NET Core 项目，先装元包：

```bash
dotnet add package TenonAdmin
```

下面是完整的最小启动代码。已有项目请将服务注册放在 `Build()` 之前，将端点映射放在 `Run()` 之前：

```csharp
using TenonAdmin.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` 负责绑配置，把 JWT、RBAC、数据权限、日志这些服务全注册上。`MapTenonAdmin` 负责挂路由、健康检查，还有 OpenAPI 文档，后者只在 dev 下挂。默认走 SQLite，零配置就能跑。

单实例开发可以使用默认内存缓存。准备部署多个实例时，按[容器化与多副本](/zh/guide/deployment/docker)接入 Redis，并核对服务注册顺序、共享存储与实例编号。

想要更细粒度的依赖控制，可以只引某一层，比如 `.AspNetCore`、`.Services`、`.SqlSugar`、`.Core`。这些包为什么这么分层、「可替换」到底怎么替，归[核心概念](/zh/guide/concepts)讲透，这里先把它跑起来就够。

> 1.0 之前 API 仍可能调整，破坏性变更会在[更新日志](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)里明确标出。开发在 `dev` 分支进行。

## 检查后端与接口描述 {#确认三个探针}

需要确认后端状态或排查连接问题时，可以检查以下端点：

```bash
# 存活探针,只看进程在不在,不碰任何依赖
curl http://localhost:5100/health

# 就绪探针:数据库 + 缓存都连通才返回 Healthy
curl http://localhost:5100/health/ready

# OpenAPI 契约,仅 Development 环境挂载,是前端 gen:api 的数据源
curl http://localhost:5100/openapi/v1.json
```

前两个应该返回 `Healthy`。`/openapi/v1.json` 返回OpenAPI 接口描述 JSON，后面生成前端类型要用到它。这个端点生产环境不挂载，线上请求它拿到 404 是预期行为，不是漏配。

## 登录，调第一个接口

`GET /api/v1/ping` 是内核里最小的受保护接口，带有效令牌才放行。登录之前，先弄清密码从哪来。

种子只在 `sys_user` 表为空时跑一次。跑 MinimalHost 是零配置启动，没有显式配密码。内核就自己生成一个 16 位随机密码。随机源是加密安全的，`0/O`、`1/l/I` 这类易混淆字符已经剔掉。它只在**建号那一次**启动的控制台日志里打印，用一个边框圈出来，仅此一次：

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
仅限不需要保留数据的本地实验环境：删掉 `backend/samples/MinimalHost/data` 下的数据库文件，重新 `dotnet run`，空库会重新播种。生产库不能这么清。需要保留数据时，请使用已有管理员账号执行密码重置；恢复前先备份。
:::

有时候你想要一个自己说了算的固定密码，比如团队共享、CI、反复删库重来这些场景。做法是把 `backend/samples/MinimalHost/appsettings.Development.json.example` 拷成 `appsettings.Development.json`，再填上 `Seed:AdminPassword`：

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "你的密码" } } }
```

这个文件装的是本地凭证，被 `.gitignore` 排除，不会进版本库。配了它，启动日志就不再打印随机密码，你直接用自己设的账号密码登。还要注意，种子只认空库。库里只要已经有任意用户，改这里也不会覆盖已存在的账号。已有账号的密码应通过密码修改或重置流程处理，不能靠修改种子配置重置。

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

超管种子不强制首次登录改密，所以 `mustChangePassword` 是 `false`。只有管理员建号、或者被重置过密码的普通用户，这个值才会是 `true`，前端据此强制跳到改密页。带上令牌调 ping 接口：

```bash
curl http://localhost:5100/api/v1/ping \
  -H "Authorization: Bearer <accessToken>"
```

返回：

```json
{ "code": 0, "data": { "pong": true, "account": "superAdmin", "at": "2026-07-...T..." } }
```

未携带有效令牌，或令牌已过期、会话已吊销时，接口返回 `401`，标准响应中的错误码为 `40006`。超级管理员通过 `sadm` 声明绕过角色权限检查；普通用户则需要在菜单管理中配置对应路由，并通过角色获得授权。完整操作见[新建业务模块](/zh/guide/business-module)。

## 换掉默认数据库

零配置默认用 SQLite，连接串是 `Data Source=./data/admin.db`，相对 ContentRoot 解析。换正式数据库不用改代码，一段 `TenonAdmin:Database` 配置说了算，改 `DbType` 和 `ConnectionString` 两项就行。`Sqlite`、`MySql`、`SqlServer`、`PostgreSQL` 都支持：

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

「换方言」仍是**一条**连接。若还要同进程挂日志库、遗留库，见[配置多数据库](/zh/guide/multi-database)。

::: warning 生产不会自动建表
`ASPNETCORE_ENVIRONMENT=Production` 时，即便开了 CodeFirst 也**不会**自动建表。这道安全闸门防的是线上误改表结构。空库首次上生产，要么临时打开 `EnableCodeFirstInProduction: true` 让它自己建一次，要么让 DBA 手工建。详见[部署指南](/zh/guide/deployment/)。
:::

内核跑通、库也换好之后，下一站是在它上面端到端加一个自己的业务模块，见[新建业务模块](/zh/guide/business-module)。
