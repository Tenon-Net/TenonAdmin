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
  <a href="https://www.nuget.org/packages/TenonAdmin"><img src="https://img.shields.io/nuget/v/TenonAdmin" alt="NuGet"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>

<p align="center">
  <a href="https://tenonadmin.52moyu.net/login"><strong>🔗 在线预览</strong></a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://tenon.52moyu.net/zh/"><strong>📖 文档</strong></a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="CHANGELOG.md"><strong>📋 更新日志</strong></a>
</p>

---

## 🎨 这是什么？

TenonAdmin 把后台的通用能力打成了 NuGet 包：用户、角色、菜单、多机构数据权限、字典配置、操作日志、文件上传……这些每个后台都要重写一遍的东西，`dotnet add package` 装进来即可。`Program.cs` 加三行，启动就是一套完整的后台接口：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

- **默认即跑**——零配置起步：自动建表、种子数据、SQLite 兜底，第一次跑连数据库都不用装；
- **按需替换**——内置服务全部走接口 + `TryAdd` 注册，不想用哪个就注册你自己的实现顶掉它，不用 fork 源码；
- **升级 = 升包**——框架修 bug、出新功能，升个包版本号的事，你的业务代码一行不用动。

传统做法是克隆一个模板仓库：几百个文件从此都要自己维护，业务代码和框架代码搅在一起，框架发了新版只能对着 diff 手动合并。TenonAdmin 反过来——通用能力走包管理，业务代码永远只是业务代码。

前端也配好了：**两套功能对等的模板**（Vue 和 React），挑一套作为项目起点。

## 🚀 快速开始

### 环境要求

- .NET 10 SDK
- Node.js 20+（跑前端模板才需要）

### 先跑起来看看

克隆仓库，后端一条命令：

```bash
dotnet run --project backend/samples/MinimalHost
```

首次启动自动建库建表、写入种子数据，并把随机生成的超管密码打印在控制台（账号 `superAdmin`）。API 就绪：http://localhost:5100

前端挑一套（或两套都起，端口不冲突）：

```bash
cd web && npm install && npm run dev            # Vue 版 → http://localhost:5173
cd web-react && npm install && npm run dev      # React 版 → http://localhost:5174
```

浏览器打开，用控制台里那对账号密码登录，即可看到完整后台。Windows 用户更省事：仓库根目录双击 `dev.bat`，后端 + 两套前端一次全起。

### 接进你自己的项目

```bash
dotnet add package TenonAdmin
```

`Program.cs` 加上面那三行，启动后 JWT 认证、RBAC、数据权限、全部管理端接口自动就位。想换数据库？改一段配置：

```jsonc
// appsettings.json
"TenonAdmin": {
  "Database": {
    "DbType": "MySql",          // Sqlite / MySql / SqlServer / PostgreSQL
    "ConnectionString": "..."
  }
}
```

### 内置实现不想用？换掉它

所有内置服务都是接口 + `TryAdd` 注册——你先注册，内置的就自动让位：

```csharp
// 比如换掉密码哈希算法:在 AddTenonAdmin 之前注册你自己的实现
builder.Services.AddSingleton<IPasswordHasher, MyPasswordHasher>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

粒度还可以更细：服务方法拆成了一个个 `virtual` 小步骤，继承内置服务、只覆写你关心的那一步就行，不用整个方法抄一遍。这套可替换性不是口号——仓库里有一组专门的契约测试锁着它。

## ✨ 后端功能

- **认证** — 账号密码 + 图形验证码、JWT + Refresh Token 轮换、登录锁定、在线会话与强制退出
- **RBAC** — 角色管理、目录／页面／按钮三级菜单、按钮级权限码、角色菜单授权
- **数据权限** — 全部 / 本机构 / 本机构及下级 / 仅本人 / 自定义，ORM 全局过滤器自动生效，业务代码不用写一行过滤
- **多应用门户** — 应用管理、独立菜单树、应用选择与切换
- **组织机构** — 机构树、职位、用户多角色与主属机构
- **消息通知** — 站内通知与公告，可按全体 / 角色 / 用户发送
- **字典与配置** — 字典类型 + 字典项 + 键值配置，缓存 + 事件驱动失效
- **日志** — 操作日志自动记录、敏感输入脱敏
- **文件管理** — 上传下载、大小限制、扩展名白名单、路径穿越防护
- **多数据库** — SQLite（默认）/ MySQL / SQL Server / PostgreSQL，改一段配置就切
- **多副本** — 可选 Redis 缓存、跨副本限流计数、每副本独立雪花机器号，横向扩容不踩坑
- **克制的依赖** — 核心包运行时只依赖 SqlSugarCore + Microsoft.*，不往你的项目里塞一堆第三方框架

## 🖥️ 前端：两套官方模板，选一套就够

同一个后端契约，配了两套完全独立的前端模板：

| | `web/` | `web-react/` |
|---|---|---|
| 框架 | Vue 3 + Naive UI | React 19 + Ant Design 6 |
| 状态 / 路由 | Pinia + vue-router | zustand + react-router |
| 多语言 | vue-i18n | react-i18next |
| 开发端口 | :5173 | :5174 |

零共享是刻意的：两套模板互不引用，连一个工具函数都不共用。用哪套就只依赖哪套，删掉另一套什么都不会发生。功能是逐页对齐移植的，两边都有：

- **合约生成 API** — OpenAPI → `schema.d.ts`，全链路类型安全，后端改了接口前端编译就报错
- **动态路由** — 后端菜单树驱动路由注册，多应用门户无缝切换
- **按钮级权限** — Vue 用 `v-auth` 指令、React 用 `<Can>` 组件，权限码同一套
- **列驱动表格** — 一个 `columns` 数组同时驱动搜索表单、字典渲染与列设置
- **设计令牌 + 亮暗双主题** — CSS 变量四层令牌，跟随系统 / 手动切换
- **三套登录页皮肤** — 开箱可选，样式隔离，不喜欢就换
- **自研组件库** — FormContainer（弹窗/抽屉二合一）、StatusSwitch（悲观更新开关）、字典三件套、OrgTreeSelect、FileUpload（分片/断点续传/秒传）、PasswordStrength、图表封装等，两边各自实现一份

## 🧩 仓库结构

| 目录 | 是什么 |
|---|---|
| `backend/` | .NET 10 内核（5 个 NuGet 包）+ 示例宿主 + 测试 |
| `web/` | Vue 3 + Naive UI 前端模板，自包含 |
| `web-react/` | React 19 + Ant Design 6 前端模板，自包含 |
| `templates/` | `dotnet new tenon-app` 项目模板 |
| `site/` | 文档站源码（VitePress，中英双语） |
| `docs/` | 设计文档与开发记录 |

## 📋 项目状态

**1.0 之前 API 仍可能调整**，破坏性变更会在[更新日志](CHANGELOG.md)中标出。开发在 `dev` 分支进行，欢迎 issue 和 PR。

## 📄 许可证

[Apache License 2.0](LICENSE)
