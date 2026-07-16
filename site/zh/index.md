---
layout: home

hero:
  name: TenonAdmin
  text: 三行代码接入完整 RBAC
  tagline: 一个零配置、可扩展的 ASP.NET Core 后台管理系统，内置 RBAC 权限管理、数据权限
  image:
    src: /icon-128.png
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
    details: 每个内置服务都以接口注册、按步骤可继承重写,不用 fork 即可替换任意一环,升级不冲突。
  - icon: 🏢
    title: 多组织数据权限
    details: 内置五种数据范围,基于 ORM 全局过滤器自动隔离,业务查询无需手写机构过滤条件。
  - icon: ⚡
    title: 零配置启动
    details: 默认 SQLite 自动建表、写种子,首次启动打印一次超管密码;换数据库只改一处配置。
  - icon: 📦
    title: 极简依赖
    details: 运行时仅依赖 SqlSugar 与 Microsoft.* 官方库,Redis、对象存储等按需引入。
  - icon: 🔐
    title: 认证与安全
    details: JWT 鉴权、图形验证码、登录锁定、强制下线、日志脱敏都在内置里,安全基线默认到位。
  - icon: 🖥️
    title: 全栈交付
    details: 配套 Vue 3 + Naive UI 管理端,支持容器化部署与多副本水平扩展。
  - icon: 🧰
    title: 组件生态
    details: ProTable、IconPicker 等通用组件已拆为独立 npm 包,任意 Vue 3 + Naive UI 项目可单装。
    link: /zh/components/
    linkText: 看看组件生态
  - icon: 🤖
    title: 辅助开发 Skills
    details: 把新增实体、搭 CRUD、替换服务的流程写成标准 skills,AI 助手或开发者照此生成符合规范的代码。
    link: /zh/community/agent-skills
    linkText: 看看 Skills
