# TenonAdmin(榫卯 Admin)

基于 ASP.NET Core + SqlSugar 的轻量企业管理系统内核 —— 装一个 NuGet 包、三行代码启动。
设计方案见 `docs/rebuild-design.md`(从旧仓迁入)。

> 当前状态:**walking skeleton(能走路的骨架)** —— 验证"三行启动 + 零配置 SQLite + CodeFirst 建表 + 种子 + /health"。业务功能(认证/RBAC/数据权限)自 M1 起补。

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
