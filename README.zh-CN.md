<!-- 本文件为 README 的中文基准版；README.md、README.ja.md 以本文件为准同步 -->

[English](README.md) | 简体中文 | [日本語](README.ja.md)

<p align="center">
  <img src="web/design-mockups/brand/icon-128.png" width="80" height="80" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center">
  <em>三行代码，为 ASP.NET Core 项目接入一套完整、可扩展的 RBAC 权限管理。</em>
</p>


<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="License"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/stargazers"><img src="https://img.shields.io/github/stars/Tenon-Net/TenonAdmin" alt="Stars"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/network/members"><img src="https://img.shields.io/github/forks/Tenon-Net/TenonAdmin" alt="Forks"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>


---

TenonAdmin 是一套基于 ASP.NET Core、SqlSugar、Vue 3、Vite 和 Naive UI 开发的后台权限管理系统。

它和传统的后台模板不太一样。TenonAdmin 不要求你复制整个项目再进行二次开发，而是把用户、角色、菜单、组织机构、数据权限、日志等通用能力封装成可接入、可替换、可扩展的模块。

在已有的 ASP.NET Core 项目中，只需要完成服务注册、应用构建和端点映射，就可以接入一套包含登录认证、RBAC 权限和管理端页面的基础能力。你可以直接使用默认实现，也可以根据业务需求替换其中的服务和流程。

## 为什么做 TenonAdmin

实际开发后台系统时，用户、角色、菜单、权限、机构和日志等功能往往需要重复实现。

直接复制一套后台模板虽然上手很快，但随着业务代码不断增加，项目通常会和模板本身深度耦合。后续想升级基础能力、同步上游改动，或者只替换其中一部分功能，都会变得比较麻烦。

TenonAdmin 希望把这些通用功能从具体业务中拆出来，让后台权限系统既能直接使用，也能比较自然地接入已有项目。

- **三行接入**：在现有 ASP.NET Core 项目中完成注册和映射，即可启用登录认证、RBAC 权限及管理端接口。
- **默认即可运行**：默认使用 SQLite，启动时自动创建表结构并初始化基础数据。
- **支持按需替换**：主要服务通过接口注册，默认实现中的关键流程支持继承和重写。
- **依赖按需引入**：运行时只依赖 SqlSugar + `Microsoft.*`，更重的能力拆成独立可选包按需添加
- **内置数据权限**：支持全部数据、本机构、本机构及下级、仅本人和自定义机构五种数据范围。
- **前后端完整提供**：后端提供权限与业务基础能力，前端提供基于 Vue 3 和 Naive UI 的管理页面。

## 快速开始

运行仓库中自带的示例项目：

```bash
dotnet run --project backend/samples/MinimalHost
```

首次启动时会自动创建数据库和初始化数据，并在控制台输出随机生成的超级管理员密码，请注意保存。

在已有的 ASP.NET Core 项目中，核心接入只需要三行代码：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

之后按照普通 ASP.NET Core 项目的方式调用 `app.Run()` 即可启动应用。

项目启动后，自动建表、初始化数据、JWT 认证、RBAC 权限、数据权限以及后台管理接口都会完成注册。

## 主要功能

- **登录认证**：账号密码登录、图形验证码、JWT、Refresh Token 轮换、登录锁定、在线会话和强制退出
- **RBAC 权限**：角色管理、目录／页面／按钮三级菜单、按钮级权限码和角色菜单授权
- **多应用门户**：应用模块管理、独立菜单树、应用选择与切换、用户默认应用
- **数据权限**：支持五种数据范围，并通过 ORM 全局过滤器在查询时自动生效
- **组织机构**：用户、机构树和职位管理，支持用户绑定多个角色并设置主属机构
- **消息通知**：站内通知与公告，可发送给全体、指定角色或指定用户，顶栏铃铛提供未读提醒和消息面板
- **字典与配置**：字典类型、字典项和键值配置，支持缓存及事件驱动的缓存失效
- **日志管理**：自动记录操作日志并对敏感输入进行脱敏，同时保留登录 IP、User-Agent 和登录结果
- **文件管理**：本地文件上传与下载、文件大小限制、扩展名白名单和路径穿越防护
- **个人中心**：修改密码、编辑个人资料和设置头像

前端使用 Vue 3 和 Naive UI，目前提供三套可切换的登录页样式。

## 扩展与定制

TenonAdmin 提供了几种不同层级的扩展方式，可以根据实际改动范围选择：

1. **修改配置**：调整 `appsettings.json` 中的 `TenonAdmin` 配置节
2. **替换服务**：注册自定义服务实现，替换系统中的默认实现
3. **继承默认实现**：继承已有服务，只重写需要调整的流程步骤
4. **扩展接口**：替换默认路由，或者增加自己的业务接口

项目同时支持实体扩展和自定义业务模块，方便在现有权限体系上继续开发实际业务。

## 技术栈

### 后端

- .NET 10（ASP.NET Core）
- SqlSugar ORM
- JWT Bearer 认证
- SQLite（默认）／MySQL／SQL Server／PostgreSQL

### 前端

- Vue 3.5 + TypeScript 5.7
- Naive UI 2.41
- Pinia 3（持久化）
- Vue Router 4
- Vue I18n
- Vite 6
- ECharts 5.6

### NuGet 包

```text
TenonAdmin.Core → TenonAdmin.SqlSugar → TenonAdmin.Services → TenonAdmin.AspNetCore → TenonAdmin
```

一般情况下直接引用 `TenonAdmin` 即可获得完整的后端能力；有更细粒度的依赖控制需求时，也可以单独引用其中的某一层。

## 项目状态

当前版本 **`0.1.0`**，已发布到 nuget.org：

```bash
dotnet add package TenonAdmin
```

后端内核、完整管理端页面、配置中心、容器化交付、多副本支持（Redis 缓存、限流计数跨副本共享、每副本独立雪花机器号）均已可用并有 CI 覆盖。

**1.0 之前 API 仍可能调整**，破坏性变更会在 [更新日志](CHANGELOG.md) 中明确标出。开发在 `dev` 分支进行。

## 项目结构

```text
tenon-admin/
├── backend/
│   ├── src/
│   │   ├── TenonAdmin.Core/            # 公共契约：接口、配置项和错误码
│   │   ├── TenonAdmin.SqlSugar/        # 数据访问：ORM、仓储和 Code First
│   │   ├── TenonAdmin.Services/        # 业务实体、服务实现和 RBAC
│   │   ├── TenonAdmin.AspNetCore/      # ASP.NET Core 集成：控制器、过滤器和 JWT
│   │   ├── TenonAdmin/                 # 完整后端元包
│   │   └── TenonAdmin.Caching.Redis/   # 可选的 Redis 缓存实现
│   ├── samples/MinimalHost/            # 最小化示例项目
│   └── tests/                          # xUnit 测试
├── web/                                # Vue 管理端
└── docs/                               # 文档与开发计划
```

## 文档

- [业务模块开发指南](docs/new-business-guide.md)
- [部署](docs/deployment.md)
- [架构与设计](docs/rebuild-design.md)
- [开发计划](docs/dev-plan.md)

## 许可证

本项目基于 [Apache License 2.0](LICENSE) 开源。
