<!-- 中文基准版；README.md、README.ja.md 与本文件保持同步。 -->

[English](README.md) | 简体中文 | [日本語](README.ja.md)

<p align="center">
  <img src="site/public/tenon-mark.png" width="96" height="96" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center"><strong>三行代码，为 ASP.NET Core 项目接入后台管理框架。</strong></p>

<p align="center">NuGet 安装 · 业务独立开发 · Vue / React 双前端</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TenonAdmin"><img src="https://img.shields.io/badge/NuGet-0.7.0-004880?logo=nuget&logoColor=white" alt="NuGet 0.7.0"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/ASP.NET_Core-512BD4?logo=dotnet&logoColor=white" alt="ASP.NET Core">
  <img src="https://img.shields.io/badge/Vue-3-4FC08D?logo=vuedotjs&logoColor=white" alt="Vue 3">
  <img src="https://img.shields.io/badge/Naive_UI-Vue-36AD6A" alt="Naive UI">
  <img src="https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=20232A" alt="React 19">
  <img src="https://img.shields.io/badge/Ant_Design-6-0170FE?logo=antdesign&logoColor=white" alt="Ant Design 6">
  <img src="https://img.shields.io/badge/SqlSugar-ORM-2F6F9F" alt="SqlSugar ORM">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions/workflows/backend-ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="dev 后端构建状态"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="Apache License 2.0"></a>
</p>

<p align="center">
  <a href="#快速开始">快速开始</a> ·
  <a href="#界面预览">界面预览</a> ·
  <a href="https://tenonadmin.52moyu.net/login">在线演示</a> ·
  <a href="https://tenon.52moyu.net/zh/">开发文档</a> ·
  <a href="CHANGELOG.md">更新日志</a>
</p>

## 介绍

TenonAdmin 是一个面向 ASP.NET Core 的后台管理框架，提供用户、角色、菜单、组织、数据权限等常用功能，配套 Vue 和 React 两套管理界面。适合用来开发企业内部系统、运营后台，也可以接入已有的 .NET 项目。

通过 NuGet 安装 `TenonAdmin` 后，在 `Program.cs` 中完成核心接入：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

框架负责注册登录认证、角色权限、数据权限和管理 API。默认使用 SQLite，首次启动会创建数据库、表和初始账号，不需要先搭建数据库服务。完整启动代码和前端运行步骤见[快速开始](#快速开始)。

客户、订单等业务实体、接口和页面写在自己的项目中。需要调整登录流程、文件存储或权限规则时，可以通过扩展接口定制；后端框架通过 NuGet 更新，业务代码与框架源码分开维护。

## 主要特点

### 在自己的项目中开发业务，也能调整框架行为

开发客户管理模块时，可以复用已有的账号、角色、菜单和文件管理，在应用项目中编写客户实体、业务接口和管理页面。业务代码不必直接写进框架源码，项目中的其他业务逻辑也可以继续保留。

遇到内置实现不适合的地方，例如文件需要存到对象存储，或登录成功后需要补充业务信息，可以替换相应服务，或继承内置服务，只修改需要调整的步骤。框架提供对应的接口和可覆写方法，不需要为了改一个处理步骤而复制整套实现。

后端框架的修复和新功能通过 NuGet 版本提供。升级时查看更新日志，按需调整扩展代码和前端接口，具体示例见[服务替换与扩展](skills/replace-service.md)。

### 控制能做什么，也控制能看到哪些数据

角色权限决定用户能访问哪些菜单、按钮和接口，数据权限决定他能查询哪些业务记录。例如，同一张客户列表，可以让部门负责人查看本部门的数据，让上级负责人查看部门及下级的数据，也可以为跨部门协作指定可访问的组织。

业务实体接入框架的数据范围约定后，使用框架支持的查询方式，就能按当前用户的角色应用过滤规则。客户查询仍然写在业务服务中，权限范围由框架统一处理；原生 SQL 和自定义数据访问需要另行落实权限检查。

仓库的独立示例项目用客户列表演示了不同账号看到不同数据的效果，参见[多组织数据权限示例](https://github.com/Tenon-Net/tenon-example/blob/dev/docs/showcase-multi-org-data-scope.md)。

### Vue 和 React，按团队习惯选择

Vue 版本使用 Vue 3 和 Naive UI，React 版本使用 React 19 和 Ant Design 6。两套前端连接同一套后端 API，开发业务时选择其中一套即可。

新增页面可以复用表格、表单、字典选择、组织与用户选择、文件上传和权限组件。例如，客户列表的筛选、分页、编辑弹窗和按钮权限，可以沿用现有页面与组件的组织方式，不必每个模块重新搭一遍。

API 类型可以从后端 OpenAPI 生成，接口变更后同步更新。两套前端各自维护依赖和组件，详细用法分别放在 [Vue 组件文档](web/COMPONENTS.md)和 [React 组件文档](web-react/COMPONENTS.md)中。

### 按业务需要接入审批流程

审批工作流通过可选包提供。可以在流程设计器中配置审批人、条件分支、并行分支和抄送，给表单字段设置各节点的查看与编辑权限。流程发布后，用户可以发起申请、处理待办，并查看审批进度和办理记录。

审批端提供退回、撤销、转办、委托和加减签等操作，流程管理员可以查看运行状态、超时和重试记录。工作流使用同一套后端 API，Vue 和 React 均提供对应界面。

使用时需要安装并注册 `TenonAdmin.Workflow`，并与 `TenonAdmin` 保持相同版本。其中的 AI 评估用于记录参考结果，不会自动同意、拒绝或推进审批。接入步骤与配置说明见[审批工作流文档](https://tenon.52moyu.net/zh/guide/workflow)。

### 给 AI 编程助手提供项目开发约定

仓库提供新增模块、实体、接口、前端页面、服务扩展和导入导出等开发 Skills，包含实现步骤、参考代码与检查要求。使用 AI 编程助手时，可以让它先读取相关说明，再按项目的实体、服务、权限和组件约定编写代码。

例如，可以这样描述一个业务需求：

> 参考 `skills/new-module.md`，新增产品管理模块，包含产品名称、编码、分类和启用状态，支持分类筛选、导入导出，并接入菜单和按钮权限。

这些 Skills 是开发说明和参考模板，不是独立的代码生成器。生成后仍需核对业务规则、权限和测试结果，入口见[开发 Skills](skills/README.md)。

## 快速开始

后端需要 .NET 10 SDK，前端建议使用 Node.js 22.12 或更高版本。首次体验可以运行仓库示例；准备接入业务时，使用下方的独立项目接入方式。

> 前端模板应与后端版本配套，扩展包与 `TenonAdmin` 使用相同版本。升级前请查看[更新日志](CHANGELOG.md)。

### 先运行完整示例

克隆 `dev` 分支并启动后端：

```bash
git clone --branch dev --single-branch https://github.com/Tenon-Net/TenonAdmin.git
cd TenonAdmin
dotnet run --project backend/samples/MinimalHost
```

后端运行在 `http://localhost:5100`。示例默认使用 SQLite，首次初始化时自动建表，并创建 `superAdmin` 账号。

控制台会打印初始随机密码，**只在首次创建账号时显示**。保存这串密码，用于后面的登录。

另开一个终端，从仓库根目录选择一套前端启动。

**Vue：**

```bash
cd web
npm install
npm run dev
```

打开 `http://localhost:5173`，使用 `superAdmin` 和控制台中的密码登录。

**React：**

```bash
cd web-react
npm install
npm run dev
```

打开 `http://localhost:5174`，使用同一账号登录。两套前端任选其一，后端只需启动一次。登录后请及时修改初始密码。

### 接入自己的 ASP.NET Core 项目

在项目目录中安装后端包：

```bash
dotnet add package TenonAdmin
```

新建宿主的最小 `Program.cs` 如下：

```csharp
using TenonAdmin.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// TenonAdmin 的核心接入
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` 注册框架服务，`MapTenonAdmin` 接入管理 API。已有项目应将这两处调用合并到原来的启动流程中，保留自己的配置、服务和业务路由。

管理界面使用配套的 Vue 或 React 模板，按所选后端版本接入。默认数据库为 SQLite；改用 MySQL、SQL Server 或 PostgreSQL 时，通过数据库配置指定类型和连接地址。

数据库设置、新建项目模板以及接入已有应用的步骤见[接入指南](https://tenon.52moyu.net/zh/guide/getting-started)。Excel、外部登录、Redis、工作流和 `TenonAdmin.Integration` 等扩展按需安装，并按各自文档完成注册和配置。系统集成包不在 `TenonAdmin` 元包中，应用凭据、开放接口、出站调用与可靠投递的启用步骤见[系统集成](https://tenon.52moyu.net/zh/guide/integration)。

## 功能一览

| 分类 | 主要功能 |
| --- | --- |
| 用户与组织 | 用户、角色、组织树、岗位，在线会话与强制下线 |
| 权限管理 | 菜单、按钮、接口权限；全部、所属组织、所属组织及下级、本人和自定义组织数据范围 |
| 登录与安全 | 账号密码、验证码、登录锁定、请求限流；按配置启用多因素认证，按需接入短信和外部登录 |
| 日常管理 | 多应用门户、字典、配置、通知公告、消息推送、操作日志与回收站 |
| 文件管理 | 上传下载、签名访问、分片上传、断点续传与秒传 |
| Excel 导入导出（可选包） | 导入模板、数据预览、单元格校验、查重、错误报告与导出列选择 |
| 定时任务 | 定时、固定间隔和单次执行；代码、HTTP 和 SQL 任务，运行日志、超时、失败重试与告警；SQL 执行需要配置开启 |
| 审批工作流（可选包） | 流程设计、动态表单、节点字段权限、审批办理、超时处理与运行记录 |
| 系统集成（可选包） | 应用凭据与 API Key、开放接口和数据范围授权、出站调用记录，以及支持重试和人工处理的可靠投递 |
| 前端支持 | Vue 与 React 两套模板，中英文、明暗主题、布局设置、业务组件与 OpenAPI 类型生成 |
| 数据与部署 | SQLite、MySQL、SQL Server、PostgreSQL，多数据库连接、独立任务进程、Docker、健康检查与 Redis 缓存扩展 |

生产环境配置和多副本部署见[部署指南](docs/deployment.md)。工作流向外部系统投递消息时，需要接入实际发送实现，具体要求见[工作流文档](https://tenon.52moyu.net/zh/guide/workflow)。

## 界面预览

点击图片查看原图。

<!-- 每行两张，使用仓库中的等比缩略图；新增截图时继续追加 <tr>，并同步原图与缩略图。 -->
<table>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/vue-admin.png"><img src="docs/screenshots/thumbs/vue-admin.png" alt="Vue 用户管理" width="480"></a>
      <br>Vue 用户管理
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/react-admin.png"><img src="docs/screenshots/thumbs/react-admin.png" alt="React 用户管理" width="480"></a>
      <br>React 用户管理
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/role-permissions.png"><img src="docs/screenshots/thumbs/role-permissions.png" alt="角色权限" width="480"></a>
      <br>角色权限
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/org-management.png"><img src="docs/screenshots/thumbs/org-management.png" alt="组织管理" width="480"></a>
      <br>组织管理
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/dictionary.png"><img src="docs/screenshots/thumbs/dictionary.png" alt="字典管理" width="480"></a>
      <br>字典管理
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/file-management.png"><img src="docs/screenshots/thumbs/file-management.png" alt="文件管理" width="480"></a>
      <br>文件管理
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/scheduled-jobs.png"><img src="docs/screenshots/thumbs/scheduled-jobs.png" alt="定时任务" width="480"></a>
      <br>定时任务
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/workflow-pending.png"><img src="docs/screenshots/thumbs/workflow-pending.png" alt="审批待办" width="480"></a>
      <br>审批待办
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/workflow-designer.png"><img src="docs/screenshots/thumbs/workflow-designer.png" alt="流程设计器" width="480"></a>
      <br>流程设计器
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/workflow-approval.png"><img src="docs/screenshots/thumbs/workflow-approval.png" alt="审批详情" width="480"></a>
      <br>审批详情
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/integration-apps.png"><img src="docs/screenshots/thumbs/integration-apps.png" alt="接入应用" width="480"></a>
      <br>接入应用
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/delivery-tasks.png"><img src="docs/screenshots/thumbs/delivery-tasks.png" alt="可靠投递" width="480"></a>
      <br>可靠投递
    </td>
  </tr>
</table>

两张截图对应当前 `TenonAdmin.Integration` 模块：左侧是接入应用管理及其凭据、权限入口，右侧是可靠投递任务与尝试记录。安装可选包并跑通完整链路的步骤见[系统集成](https://tenon.52moyu.net/zh/guide/integration)。

## 文档与业务示例

[tenon-example](https://github.com/Tenon-Net/tenon-example) 是一个独立业务项目：后端通过 NuGet 使用 TenonAdmin，前端基于管理端模板，业务部分包含 CRM 模块。需要了解框架之外的业务代码怎么组织，可以从这个项目开始。

[在线演示](https://tenonadmin.52moyu.net/login) 使用该项目独立部署，功能随示例项目版本更新；体验当前 `dev` 分支时，以本地运行的仓库示例为准。

| 要做的事 | 文档 |
| --- | --- |
| 接入框架与配置数据库 | [快速开始](https://tenon.52moyu.net/zh/guide/getting-started) |
| 新增业务模块 | [模块开发说明](skills/new-module.md) · [全部开发 Skills](skills/README.md) |
| 开发前端页面 | [Vue 组件](web/COMPONENTS.md) · [React 组件](web-react/COMPONENTS.md) |
| 调整内置服务 | [服务替换与扩展](skills/replace-service.md) |
| 添加导入导出和审批 | [Excel 导入导出](skills/wire-import-export.md) · [审批工作流](https://tenon.52moyu.net/zh/guide/workflow) |
| 了解后端结构 | [架构说明](https://tenon.52moyu.net/zh/backend/architecture) |
| 部署与升级 | [部署指南](docs/deployment.md) · [更新日志](CHANGELOG.md) |

<details>
<summary>仓库目录</summary>

```text
TenonAdmin/
├── backend/
│   ├── src/          后端 NuGet 包源码
│   ├── samples/      示例宿主与独立任务进程
│   └── tests/        后端测试
├── web/              Vue 管理端
├── web-react/        React 管理端
├── templates/        后端项目模板
├── skills/           开发说明与参考代码
├── site/             文档站点
└── docs/             架构、部署和开发参考
```

</details>

## 参与贡献

开发在 `dev` 分支进行。发现问题请提交 [Issue](https://github.com/Tenon-Net/TenonAdmin/issues)，附上使用版本、复现步骤和相关日志；代码改动向 `dev` 提交 PR。

## 版权与许可证

Copyright © Tenon-Net

本项目采用 [Apache License 2.0](LICENSE)，允许商业使用、修改和分发。

分发本项目或其衍生作品时，请附带许可证副本，在修改过的文件中标明改动，并在分发的源码中保留适用的版权、专利、商标及归属声明。涉及 NOTICE 文件的归属声明时，按许可证第 4 条处理。

第三方组件和素材遵循各自的许可证。
