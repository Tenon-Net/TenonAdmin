# 从零跑通第一个接口

本篇带你把 TenonAdmin 内核跑起来,拿到一个可用的令牌,调通第一个受保护接口。全程零配置,不用装数据库。

::: tip 前提条件
- .NET 10 SDK
- 想顺带跑前端的话还需要 Node.js(建议 20+)
:::

## 1. 跑通仓库自带的示例项目

克隆仓库后,直接运行 `backend/samples/MinimalHost`——这是仓库里最小化的示例宿主,`Program.cs` 只有几行接线:

```bash
dotnet run --project backend/samples/MinimalHost
```

首次启动会自动:

- 用默认 SQLite 建表(CodeFirst,库文件落在 `backend/samples/MinimalHost` 的 `./data/` 下)
- 写入种子数据(菜单、角色、超级管理员账号等)
- 在控制台**打印一次随机生成的超级管理员密码**——只打印这一次,记得复制下来

服务默认监听 `http://localhost:5100`。

::: warning 密码只打印一次
没记下来也不用慌,删掉 `backend/samples/MinimalHost/data` 目录下的数据库文件重新 `dotnet run` 即可重新播种(仅限本地实验环境这么干,生产库不能这样清)。
:::

## 2. 验收三个端点

服务起来后,先确认这三个探针都通:

```bash
# 存活探针,不跑任何依赖检查
curl http://localhost:5100/health

# 就绪探针:数据库 + 缓存都连通才算 Healthy
curl http://localhost:5100/health/ready

# OpenAPI 契约(仅 Development 环境挂载,是前端 gen:api 的数据源)
curl http://localhost:5100/openapi/v1.json
```

前两个应该都返回 `Healthy`。`/openapi/v1.json` 返回一大坨 JSON,是后面前端生成类型要用的契约源。

## 3. 登录换令牌

`GET /api/v1/ping` 是内核里最小的受保护接口——带有效令牌才放行。默认没开图形验证码(`Security:Captcha:Enabled` 默认关),登录只需要账号密码:

```bash
curl -X POST http://localhost:5100/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"account":"superAdmin","password":"<控制台打印的那串密码>"}'
```

响应信封里的 `data.accessToken` 就是令牌:

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": true } }
```

`superAdmin` 是默认超管账号(`TenonAdmin:Seed:AdminAccount` 的默认值),首登 `mustChangePassword` 会是 `true`——这里只是打通接口,暂时不用管它。

## 4. 调第一个受保护接口

带上刚才拿到的令牌:

```bash
curl http://localhost:5100/api/v1/ping \
  -H "Authorization: Bearer <accessToken>"
```

返回:

```json
{ "code": 0, "data": { "pong": true, "account": "superAdmin", "at": "2026-07-...T..." } }
```

不带令牌,或令牌过期/被吊销,拿到的是 `401`(标准信封,`code=40006`)。超管(`sadm` 声明)自动绕过后续的 `[RolePermission]` 权限码校验;普通用户则要先在菜单管理里挂上对应路由、在角色管理里授权,才能调通同一个接口——这条链路的完整写法见[新建业务模块](/zh/tutorial/business-module)。

## 5. 三行代码接入已有 ASP.NET Core 项目

上面跑的是仓库自带的示例;真要把内核接进你自己的项目,核心只有三行:

```bash
dotnet add package TenonAdmin
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` 绑配置、注册 JWT/RBAC/数据权限/日志等全部服务;`MapTenonAdmin` 挂路由、健康检查和(dev 下的)OpenAPI 文档。默认 SQLite、零配置即可跑,换库改 `appsettings.json` 里的 `TenonAdmin:Database` 就行。

当前版本是 **`0.1.0`**,已发布到 nuget.org;1.0 之前 API 仍可能调整,开发在 `dev` 分支进行。

## 6. 顺带把前端跑起来

前端是仓库 `web/` 目录下的 Vue 3 + Naive UI 管理端模板:

```bash
cd web
npm install
npm run dev        # Vite 起在 :5173,自动反代 /api、/openapi 到后端 :5100(可用 TENON_API_TARGET 覆盖目标)
```

浏览器打开 `http://localhost:5173`,用上面同一个超管账号密码登录,就能看到完整的后台管理界面了。

::: tip 一条命令起全部
仓库根的 `dev.bat` 会在两个窗口里分别拉起后端 + 前端(首次运行顺带装好前端依赖);`stop.bat` 停掉它们。
:::

## 下一步

- 想在内核上加自己的业务表和接口 → [端到端加一个业务模块](/zh/tutorial/business-module)
- 想给前端加一个真实页面 → [前端加一个页面](/zh/tutorial/frontend-page)
- 想把它发到服务器 → [容器化部署一条龙](/zh/tutorial/docker-deploy)
- 想搞懂「可替换」到底怎么替换、包为什么这么分层 → [核心概念](/zh/guide/concepts)
