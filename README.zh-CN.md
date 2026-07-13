<!-- 保持与 README.md 同步 -->

[English](README.md) | 简体中文 | [日本語](README.ja.md)

<p align="center">
  <img src="web/design-mockups/brand/tenon-logo.svg" width="80" height="80" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin（榫卯 Admin）</h1>

<p align="center">
  <em>一个 NuGet 包、三行代码,启动一套企业级后台管理系统内核。</em>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="License"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/stargazers"><img src="https://img.shields.io/github/stars/Tenon-Net/TenonAdmin" alt="Stars"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/network/members"><img src="https://img.shields.io/github/forks/Tenon-Net/TenonAdmin" alt="Forks"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>

---

TenonAdmin 是基于 ASP.NET Core + SqlSugar+Vue3+Vite+Naive UI做的一套后台管理模板，开箱即用，内置登录认证、RBAC 权限、多机构数据权限和 Vue 管理端模板——而且每一项都能被你替换或覆写。如果你要做内部管理系统，不想每次都从用户和权限开始搭，可以直接用它。它既可以直接运行，也可以接到已有的 ASP.NET Core 项目里。

## 为什么做这个

市面上 .NET 后台管理框架不少,但大多数给你一个跑起来的应用,然后就把你锁在里面了。想改?fork 仓库、追上游更新,带一堆你没用到的依赖。TenonAdmin 换了个思路——它是个 NuGet 包,你的项目引用它,而不是反过来。

- **三行接入** —— 不用搭脚手架、不用抄模板。`AddTenonAdmin()` + `MapTenonAdmin()`,认证、RBAC、管理 UI 全齐。
- **零配置起步** —— 默认 SQLite、自动建表、首启生成随机超管密码。`dotnet run` 直接能登。
- **想换就换** —— 服务面向接口、方法 `virtual`、长流程拆小步,你可以只重写其中一步而不用复制整个方法。四层覆写:配置 → 服务替换 → 继承覆写 → 端点覆写。
- **不绑架技术栈** —— 运行时只有 SqlSugar + `Microsoft.*`。Redis、MQTT、国密算法都隔离在可选包里,用到再引。
- **多机构数据权限** —— 多数后台框架要么不做、要么做个样子。这里是五种数据范围(全部 / 本机构 / 本机构及以下 / 仅本人 / 自定义),按角色配置,在 ORM 查询层自动生效。

## 快速开始

运行自带的示例:

```bash
dotnet run --project backend/samples/MinimalHost
# 首次启动会在控制台打印随机超管密码,注意看
```

在你自己的项目里,`Program.cs` 只需三行:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

自动建库建表、种子数据、JWT 认证、RBAC 全部就绪。接下来看 [新建业务模块指南](docs/new-business-guide.md) 添加你自己的实体和接口。

## 功能一览

- **认证** —— 账密登录 + 图形验证码、JWT + Refresh Token 轮换、登录锁定、在线用户列表 / 强退
- **RBAC** —— 角色、三级菜单(目录 / 页面 / 按钮)、按钮级权限码、角色-菜单授权
- **多应用门户** —— 模块管理、菜单按应用分区、登录后选 / 切应用、每用户默认应用
- **多机构数据权限** —— 五种数据范围,按角色配置,通过全局 ORM 过滤器在查询层自动生效
- **组织** —— 用户、机构(树)、职位;用户可多角色、设主属机构
- **字典 / 配置** —— 字典类型与字典项、键值系统配置(带缓存 + 事件驱动失效)
- **日志** —— 操作日志(自动记录,输入脱敏)、登录日志(IP / UA / 结果)
- **文件** —— 本地上传 / 下载、大小限制、后缀白名单、路径穿越防护
- **个人中心** —— 改密码、改资料、头像

前端为 Vue 3 + Naive UI 管理端模板,带三套可切换的登录页皮肤。

## 定制覆写

四层覆写能力,按侵入程度递增:

1. **配置覆写** —— 改 `appsettings.json` 的 `TenonAdmin` 节
2. **服务替换** —— 在 `AddTenonAdmin()` 之前注册你的实现,`TryAdd` 保证你的优先
3. **继承覆写** —— 继承默认实现,只重写模板方法里的某一步
4. **端点覆写** —— 替换或扩展默认路由

同时支持实体扩展与自定义业务模块——[详见设计文档](docs/rebuild-design.md)。

## 技术栈

**后端**

- .NET 10 (ASP.NET Core)
- SqlSugar ORM
- JWT Bearer 认证
- 雪花 ID
- OpenAPI(前端契约来源)
- SQLite(默认）/ MySQL / SQL Server / PostgreSQL

**前端**

- Vue 3.5 + TypeScript 5.7
- Naive UI 2.41
- Pinia 3（持久化）
- Vue Router 4 · Vue I18n
- Vite 6
- ECharts 5.6
- openapi-fetch（契约生成的 API 客户端）
- OxLint

**NuGet 包（5 个）**

```
TenonAdmin.Core → TenonAdmin.SqlSugar → TenonAdmin.Services → TenonAdmin.AspNetCore → TenonAdmin
```

装 `TenonAdmin` 拿全套;也可以只引用单层做更细的控制。

## 项目状态

M3 已完成——后端内核、全套前端管理页面、配置中心均可用。当前版本 `0.0.1-preview`。

尚未发布到 nuget.org,现阶段请从源码运行。1.0 之前 API 可能调整。开发在 `dev` 分支进行。

## 项目结构

```
tenon-admin/
├── backend/
│   ├── src/
│   │   ├── TenonAdmin.Core/            # 契约层：接口、Options、ErrorCode
│   │   ├── TenonAdmin.SqlSugar/        # 数据层：ORM、仓储、CodeFirst
│   │   ├── TenonAdmin.Services/        # 业务层：实体、服务实现、RBAC
│   │   ├── TenonAdmin.AspNetCore/      # 宿主集成：控制器、过滤器、JWT
│   │   ├── TenonAdmin/                 # 元包（装这个拿全套）
│   │   └── TenonAdmin.Caching.Redis/   # 可选：Redis 缓存
│   ├── samples/MinimalHost/            # 示例项目（三行启动）
│   └── tests/                          # xUnit 测试
├── web/                                # Vue 管理端前端
└── docs/                               # 设计文档、指南、路线图
```

## 文档

- [新建业务模块指南](docs/new-business-guide.md)
- [部署](docs/deployment.md)
- [架构与设计](docs/rebuild-design.md)
- [开发计划与路线图](docs/dev-plan.md)

## 许可证

[Apache-2.0](LICENSE)
