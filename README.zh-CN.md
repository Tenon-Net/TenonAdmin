<!-- 本文件为 README 的中文基准版；README.md、README.ja.md 以本文件为准同步 -->

[English](README.md) | 简体中文 | [日本語](README.ja.md)

<p align="center">
  <img src="web/design-mockups/brand/icon-128.png" width="96" height="96" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center"><strong>三行代码，为 ASP.NET Core 项目接入包含 RBAC 与数据权限的后台管理框架。</strong></p>

<p align="center">NuGet 安装 · 服务替换与业务扩展 · Vue / React 双前端</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TenonAdmin"><img src="https://img.shields.io/nuget/v/TenonAdmin" alt="NuGet 版本"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions/workflows/backend-ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="后端构建状态"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="Apache License 2.0"></a>
</p>

<p align="center">
  <a href="#快速开始"><strong>快速开始</strong></a> ·
  <a href="https://tenonadmin.52moyu.net/login"><strong>在线演示</strong></a> ·
  <a href="https://tenon.52moyu.net/zh/"><strong>文档</strong></a>
</p>

## 框架介绍

TenonAdmin 是面向 ASP.NET Core 的后台管理框架，将用户、角色、菜单、组织数据权限、字典、配置、操作日志和文件管理封装为 NuGet 包，前端提供 Vue 和 React 两套模板。

安装 `TenonAdmin` 包，在 `Program.cs` 中注册服务并映射端点：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

框架负责通用管理功能，应用项目承载业务实体、服务和页面。通过接口替换服务，或继承内置服务修改处理步骤，可以调整框架行为。框架更新通过 NuGet 版本交付，兼容性变更见更新日志。

- **SQLite 起步**：首次运行创建数据库和表，加载种子数据。
- **服务定制**：通过依赖注入和 `virtual` 方法替换服务或处理步骤，定制代码放在应用项目中。
- **包版本升级**：通过 NuGet 获取修复和新功能，按更新日志处理兼容性变更。
- **前端模板**：选择 Vue 3 + Naive UI 或 React 19 + Ant Design 6，使用配套的表格、表单和权限组件开发业务页面。

## 快速开始

后端需要 .NET 10 SDK，前端需要 Node.js 22.12+。

### 接入现有项目

在 ASP.NET Core 项目目录中安装框架：

```bash
dotnet add package TenonAdmin
```

将上面的服务注册和端点映射加入 `Program.cs`，保留项目的应用创建与启动代码。JWT 认证、RBAC、数据权限及管理 API 由框架注册。

数据库配置和接入步骤见[接入指南](https://tenon.52moyu.net/zh/guide/getting-started)。新项目可以使用指南中的 `dotnet new tenon-app` 模板创建后端宿主。

### 运行完整示例

克隆仓库并启动后端：

```bash
git clone https://github.com/Tenon-Net/TenonAdmin.git
cd TenonAdmin
dotnet run --project backend/samples/MinimalHost
```

后端地址为 http://localhost:5100。示例采用 SQLite，首次启动创建数据库和表、加载种子数据，并在控制台打印 `superAdmin` 的随机密码。

在另一个终端中，从仓库根目录选择一套前端启动。

**Vue** — http://localhost:5173

```bash
cd web
npm install
npm run dev
```

**React** — http://localhost:5174

```bash
cd web-react
npm install
npm run dev
```

使用 `superAdmin` 和控制台中的密码登录。Windows 用户可以运行根目录的 `dev.bat`，启动后端和两套前端。

## 特色功能

### 替换服务，扩展业务

密码哈希、文件存储等服务通过接口提供。将自己的实现注册在 `AddTenonAdmin` 之前，框架会使用该实现。需要修改某个处理步骤时，可以继承内置服务，覆盖对应的 `virtual` 方法。

服务实现放在应用项目中，框架代码由 NuGet 包维护。服务替换、业务实体发现和控制器发现有契约测试覆盖。[查看服务替换示例](skills/replace-service.md)。

### 接口授权与数据范围

用户访问同一张客户列表，查询结果由角色的数据范围决定。业务实体实现 `IOrgScoped` 或继承 `DataEntity` 后，SqlSugar 全局过滤器会为查询附加组织条件。业务服务负责查询逻辑，框架负责应用数据范围。

菜单、按钮和后端接口使用 HTTP 方法与路由组成的权限码。接口访问权限控制操作入口，数据范围控制用户能访问的业务记录。

### Vue 和 React，选你熟悉的

Vue 模板采用 Vue 3 和 Naive UI，React 模板采用 React 19 和 Ant Design 6。两套模板拥有各自的依赖、路由、状态和组件，连接同一套后端 API。

API 类型从 OpenAPI 生成。业务页面可以复用搜索表格、表单、字典、组织与用户选择、文件上传和导入向导等组件。

### 审批工作流

通过流程设计器配置审批节点、条件分支、并行分支和抄送，设置动态表单与节点字段权限。审批端提供发起、待办、已办、退回、撤销、转办、委托、加减签和催办。

运行记录包含审批历史、超时动作和执行重试信息。AI 决策评估支持兼容 OpenAI 的模型服务，结果用于审批参考与审计；审批决定和流程推进由审批流程控制。

### 第三方系统集成

开放接口面向调用方管理应用凭据、接口授权和业务数据范围。第三方调用面向外部服务管理目标地址、凭据和调用记录。

需要保障投递的业务，可以把业务数据与投递记录放在同一数据库事务中提交，由后台发送。后续处理依据接收方的幂等和结果查询能力，选择重试、查询或人工核对，并记录每次尝试。

### Vibe Coding：按项目约定写业务

仓库提供实体、CRUD、服务替换、定时任务、导入导出和系统集成等[开发技能](skills/README.md)，包含任务步骤、代码参考和验证要求。

AI 助手据此使用项目的实体基类、服务接口、权限规则和前端组件，生成业务模块。业务规则和生成结果由开发者审查。

## 功能一览

| 分类 | 功能 |
| --- | --- |
| 认证与会话 | 账号密码、图形验证码、JWT 与刷新令牌轮换、在线会话、强制下线；Cookie 会话、短信登录和外部登录按配置接入 |
| 认证安全 | 登录锁定、请求限流、TOTP、恢复码、账号 MFA 策略与高敏操作身份验证 |
| 权限与组织 | 角色、菜单、按钮权限、组织树、岗位；全量、所属组织、所属组织及下级、本人、自定义组织五种数据范围 |
| 日常管理 | 多应用门户、字典、配置、通知公告、SignalR 消息推送、操作日志、敏感输入脱敏、审计字段与回收站 |
| 文件与 Excel | 上传下载、签名访问、分片上传、断点续传、秒传；导入模板、数据预览、单元格校验、查重、错误报告和导出列选择 |
| 审批工作流 | 流程设计、动态表单、节点字段权限、审批处理、超时动作、Webhook、运行监控与 AI 决策评估 |
| 系统集成 | 应用凭据、开放接口授权、业务数据范围、应用限流、第三方调用、事务内投递记录与处理日志 |
| 定时任务 | 六段 cron、固定间隔、单次触发；代码、HTTP 和 SQL 任务，SQL 执行需配置开启；超时、重试、日志、失败告警与独立 Worker |
| 前端模板 | Vue 3 + Naive UI、React 19 + Ant Design 6；中英文、明暗主题、布局设置、业务组件与 OpenAPI 类型生成 |
| 数据与部署 | SQLite、MySQL、SQL Server、PostgreSQL；CodeFirst、多数据库连接、Redis 共享缓存、数据库租约、多副本部署、Docker 与健康检查 |
| 开发工具 | 后端项目模板、AI 开发技能；Vue ProTable 与 IconPicker 独立 npm 包 |

## 界面预览

<table>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/vue-admin.png"><img src="docs/screenshots/vue-admin.png" alt="Vue 用户管理" width="480"></a><br>Vue 用户管理</td>
    <td width="50%" align="center"><a href="docs/screenshots/react-admin.png"><img src="docs/screenshots/react-admin.png" alt="React 用户管理" width="480"></a><br>React 用户管理</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/workflow-designer.png"><img src="docs/screenshots/workflow-designer.png" alt="流程设计器" width="480"></a><br>流程设计器</td>
    <td width="50%" align="center"><a href="docs/screenshots/workflow-approval.png"><img src="docs/screenshots/workflow-approval.png" alt="审批详情" width="480"></a><br>审批详情</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/role-permissions.png"><img src="docs/screenshots/role-permissions.png" alt="角色权限" width="480"></a><br>角色权限</td>
    <td width="50%" align="center"><a href="docs/screenshots/integration-apps.png"><img src="docs/screenshots/integration-apps.png" alt="接入应用" width="480"></a><br>接入应用</td>
  </tr>
</table>

<details>
<summary>查看其他页面</summary>

<table>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/org-management.png"><img src="docs/screenshots/org-management.png" alt="组织管理" width="480"></a><br>组织管理</td>
    <td width="50%" align="center"><a href="docs/screenshots/dictionary.png"><img src="docs/screenshots/dictionary.png" alt="字典管理" width="480"></a><br>字典管理</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/file-management.png"><img src="docs/screenshots/file-management.png" alt="文件管理" width="480"></a><br>文件管理</td>
    <td width="50%" align="center"><a href="docs/screenshots/scheduled-jobs.png"><img src="docs/screenshots/scheduled-jobs.png" alt="定时任务" width="480"></a><br>定时任务</td>
  </tr>
  <tr>
    <td width="50%" align="center"><a href="docs/screenshots/workflow-pending.png"><img src="docs/screenshots/workflow-pending.png" alt="审批待办" width="480"></a><br>审批待办</td>
    <td width="50%" align="center"><a href="docs/screenshots/delivery-tasks.png"><img src="docs/screenshots/delivery-tasks.png" alt="可靠投递" width="480"></a><br>可靠投递</td>
  </tr>
</table>

</details>

## 在线体验与示例项目

[打开在线演示](https://tenonadmin.52moyu.net/login) · [查看示例项目源码](https://github.com/Tenon-Net/tenon-example)

演示站点使用独立应用 `tenon-example`：后端通过 NuGet 接入框架，前端基于管理端模板，业务部分包含 CRM 模块。

使用登录页中的业务账号访问客户列表，可以观察组织与数据范围对查询结果的影响。示例中的业务查询和权限配置见[同一个查询，三个数字](https://github.com/Tenon-Net/tenon-example/blob/dev/docs/showcase-multi-org-data-scope.md)。

## 仓库目录

```text
TenonAdmin/
├── backend/                           # .NET 后端
│   ├── src/                           # NuGet 包源码
│   │   ├── TenonAdmin/                # 安装入口，引用 ASP.NET Core 集成包
│   │   ├── TenonAdmin.Core/           # 接口、配置、结果与错误码
│   │   ├── TenonAdmin.SqlSugar/       # 数据访问、建表与全局过滤器
│   │   ├── TenonAdmin.Services/       # 管理实体、业务服务与种子数据
│   │   ├── TenonAdmin.AspNetCore/     # 认证、控制器与宿主集成
│   │   ├── TenonAdmin.Workflow/       # 审批工作流
│   │   ├── TenonAdmin.Integration/    # 开放接口、第三方调用与可靠投递
│   │   ├── TenonAdmin.Excel/          # Excel 导入导出
│   │   ├── TenonAdmin.Caching.Redis/  # Redis 缓存
│   │   └── TenonAdmin.Auth.*/         # GitHub、企业微信、钉钉、微信登录
│   ├── samples/                       # 示例宿主
│   │   ├── MinimalHost/               # 后台 API 示例
│   │   ├── WorkerHost/                # 独立任务进程
│   │   ├── IntegrationSample/         # 系统集成示例
│   │   └── IntegrationMockPartner/    # 集成示例的模拟对接方
│   ├── tests/                         # 后端测试与测试宿主
│   ├── Directory.Packages.props       # 后端依赖版本
│   └── TenonAdmin.slnx                # 后端解决方案
├── web/                               # Vue 3 + Naive UI 管理端模板
├── web-react/                         # React 19 + Ant Design 6 管理端模板
├── templates/                         # dotnet new 项目模板
├── skills/                            # AI 开发技能与参考模板
├── site/                              # 文档站点
├── docs/                              # 架构、部署、截图与开发参考
├── scripts/                           # 契约检查、测试与验证脚本
├── .github/workflows/                 # CI 与发布工作流
├── docker-compose.yml                 # 容器部署配置
├── docker-compose.scale.yml           # 多副本部署配置
└── LICENSE                            # Apache-2.0 许可证
```

`web/` 和 `web-react/` 各自维护依赖与构建配置。业务项目通过 NuGet 引用后端包，选择一套前端模板作为开发起点。

## 文档与进阶

| 要做的事 | 参考文档 |
| --- | --- |
| 接入项目 | [接入指南](https://tenon.52moyu.net/zh/guide/getting-started) |
| 开发业务 | [开发技能与参考模板](skills/README.md) · [Vue 组件](web/COMPONENTS.md) · [React 组件](web-react/COMPONENTS.md) |
| 替换服务 | [服务替换](skills/replace-service.md) |
| 接入审批与外部系统 | [审批工作流](https://tenon.52moyu.net/zh/guide/workflow) · [系统集成](https://tenon.52moyu.net/zh/guide/integration) · [Excel 导入导出](skills/wire-import-export.md) |
| 了解架构 | [架构说明](https://tenon.52moyu.net/zh/backend/architecture) · [运行时架构图](docs/architecture/tenon-runtime.zh-CN.architecture.html) |
| 部署与升级 | [部署指南](docs/deployment.md) · [更新日志](CHANGELOG.md) |

## 参与贡献

开发分支为 `dev`。问题反馈通过 [GitHub Issues](https://github.com/Tenon-Net/TenonAdmin/issues) 提交，代码改动向 `dev` 提交 PR。

## 版权与许可证

Copyright © Tenon-Net

本项目采用 [Apache License 2.0](LICENSE)，允许商业使用、修改和分发。

分发本项目或其衍生作品时，请附带许可证副本，在修改过的文件中标明改动，并在分发的源码中保留适用的版权、专利、商标及归属声明。涉及 NOTICE 文件的归属声明时，按许可证第 4 条处理。

第三方组件和素材遵循各自的许可证。
