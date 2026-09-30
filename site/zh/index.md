---
layout: home

hero:
  name: TenonAdmin
  text: 为 ASP.NET Core 项目接入后台管理
  tagline: 通过 NuGet 接入账号、角色和数据权限，在自己的项目中开发业务，前端可选 Vue 或 React
  image:
    src: /tenon-mark.png
    alt: TenonAdmin
  actions:
    - theme: brand
      text: 快速开始
      link: /zh/guide/getting-started
    - theme: alt
      text: 在线预览
      link: https://tenonadmin.52moyu.net/login
    - theme: alt
      text: GitHub
      link: https://github.com/Tenon-Net/TenonAdmin

features:
  - icon: 🧩
    title: 可插拔架构
    details: 需要调整登录或文件存储时，通过接口或可覆写方法扩展默认实现。业务代码独立维护，升级时按变更记录检查兼容性。
  - icon: 🏢
    title: 多组织数据权限
    details: 同一张客户列表，可以让员工只看自己的记录，让主管查看本部门的数据。接入数据范围的查询会自动过滤，原生 SQL 需自行检查权限。
  - icon: 🔀
    title: 审批工作流
    details: 可选包提供流程设计、发起与审批，Vue 和 React 模板都有对应页面。
    link: /zh/guide/workflow
    linkText: 跑通一次审批
  - icon: 🔗
    title: 系统集成
    details: 为外部系统注册应用、分配接口权限，并通过发送任务跟踪数据投递结果。
    link: /zh/guide/integration
    linkText: 接通第一个外部系统
  - icon: 🔭
    title: 有真实参考应用
    details: 独立示例 tenon-example 展示如何安装后端包、开发 CRM 模块并部署。可以对照实体、接口和页面，学习完整业务的组织方式。
    link: https://github.com/Tenon-Net/tenon-example
    linkText: 看看它怎么写的
  - icon: ⚡
    title: 零配置启动
    details: 本地示例默认使用 SQLite，首次启动自动创建表、菜单和管理员账号。无需先安装数据库服务，即可体验后台。
  - icon: 📦
    title: 极简依赖
    details: 核心包的运行时依赖限于 SqlSugar 和 Microsoft.*。Excel、工作流等能力通过可选包接入，按业务需要安装。
  - icon: 🔐
    title: 认证与安全
    details: JWT 鉴权、登录锁定、请求限流、强制下线、日志脱敏，默认全都在。图形验证码内置三种，按需开启。
  - icon: 🖥️
    title: 全栈交付
    details: 配套两套各自独立的管理端模板（Vue 3 + Naive UI 或 React 19 + Ant Design），二选一，支持容器化部署与多副本水平扩展。
  - icon: 🧰
    title: 组件生态
    details: ProTable、IconPicker 这些通用组件已经拆成独立 npm 包，任意 Vue 3 + Naive UI 项目都能单装。
    link: /zh/components/
    linkText: 看看组件生态
  - icon: 🤖
    title: 辅助开发 Skills
    details: 开发 Skills 提供实体、接口、页面和服务扩展的步骤与检查要求。可供开发者或 AI 助手参考，生成后仍需验证业务规则与权限。
    link: /zh/community/agent-skills
    linkText: 看看 Skills
---

## 从你的目标开始

| 你现在想做什么 | 推荐入口 |
|---|---|
| 先看看后台如何运行 | [快速开始](/zh/guide/getting-started)：启动示例并登录 |
| 判断是否适合自己的项目 | [核心概念](/zh/guide/concepts)：了解框架与业务的分工 |
| 开发第一个业务功能 | [业务模块](/zh/guide/business-module) → [前端页面](/zh/guide/frontend-page) |
| 调整默认实现 | [替换内置服务](/zh/guide/replace-service) |
| 准备部署或升级 | [部署指南](/zh/guide/deployment/)与[更新日志](/zh/changelog) |

只负责前端时，从 [Vue 入门](/zh/frontend/getting-started)或 [React 入门](/zh/frontend-react/getting-started)开始：复用已有接口做一个小页面，再学习菜单与权限。对接外部业务系统则从[系统集成](/zh/guide/integration)开始。
