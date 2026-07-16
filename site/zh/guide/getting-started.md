# 快速开始

TenonAdmin 是一套基于 ASP.NET Core、SqlSugar、Vue 3、Vite 和 Naive UI 的后台权限管理内核。它不要求你复制整个项目再二次开发,而是把用户、角色、菜单、组织机构、数据权限、日志等通用能力封装成**可接入、可替换、可扩展**的模块。

## 先跑一遍示例

仓库自带一个最小化示例项目,克隆后直接运行:

```bash
dotnet run --project backend/samples/MinimalHost
```

首次启动会自动创建数据库、初始化数据,并在控制台输出**随机生成的超级管理员密码**——请注意保存,它只打印这一次。默认监听 `http://localhost:5100`。

## 三行代码接入已有项目

在已有的 ASP.NET Core 项目里,核心接入只需三行:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

之后照常 `app.Run()` 即可。启动后,自动建表、初始化数据、JWT 认证、RBAC 权限、数据权限以及后台管理接口都会完成注册。

## 安装

当前版本 **`0.1.0`**,已发布到 nuget.org。一般直接引用元包即可获得完整后端能力:

```bash
dotnet add package TenonAdmin
```

有更细粒度的依赖控制需求时,也可以单独引用其中某一层(见[核心概念 · 包分层](/zh/guide/concepts#包分层))。

## 前端

前端是一套基于 Vue 3 + Naive UI 的管理端模板,位于仓库 `web/` 目录:

```bash
cd web
npm install
npm run dev        # Vite 起在 :5173,自动反代 /api 与 /openapi 到后端 :5100
```

## 下一步

- 想理解「可替换」到底怎么替换、包为什么这么分层 → [核心概念](/zh/guide/concepts)
- 要把它发到服务器 → [部署](/zh/guide/deployment/)
- 要在内核之上加自己的业务 → [新建业务模块](/zh/guide/new-business/)
- fork 了仓库、想在 `web/` 上跟上上游更新 → [同步你的 Fork](/zh/guide/sync-fork)

> 1.0 之前 API 仍可能调整,破坏性变更会在 [更新日志](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) 中明确标出。开发在 `dev` 分支进行。
