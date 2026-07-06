# TenonAdmin(榫卯 Admin)

基于 ASP.NET Core + SqlSugar 的轻量企业管理系统内核 —— 装一个 NuGet 包、三行代码启动。

> 📐 设计方案:`docs/rebuild-design.md` · 🗺️ 开发进度与任务队列:`docs/dev-plan.md`
>
> 当前状态:**M1 进行中,认证最小闭环已跑通** —— 三行启动 + 零配置 SQLite + CodeFirst 建表 +
> 幂等种子(首启打印随机超管密码)+ JWT 登录 + `[RolePermission]` 授权管道。
> RBAC/组织/数据权限等按 `dev-plan.md` 任务队列推进;开发在 `dev` 分支。

## 快速开始

```bash
dotnet run --project backend/samples/MinimalHost
# 启动后:自动在 backend/samples/MinimalHost/data/admin.db 建库、建 sys_schema_version 表并写种子
# 访问 http://localhost:5xxx/health -> {"status":"ok","app":"TenonAdmin"}
```

## 结构(v1 monorepo)

```
backend/                          后端(独立一层)
├─ src/    TenonAdmin.Core / .SqlSugar / .Services / .AspNetCore / TenonAdmin(元包)
├─ samples/MinimalHost            三行启动样例(即验收基准)
└─ tests/                         xunit(后续)
web/                              Vue Naive UI 前端(后续)
docs/                             设计文档
```

用户项目 `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```
