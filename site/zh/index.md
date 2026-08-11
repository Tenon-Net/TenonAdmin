---
layout: home

hero:
  name: TenonAdmin
  text: 三行代码接入完整 RBAC
  tagline: 零配置起步，RBAC、数据权限内置，架构还能按需替换扩展
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
    details: 每个内置服务都以接口注册，还能按步骤继承重写。不 fork 就能替换任意一环，升级也不冲突。
  - icon: 🏢
    title: 多组织数据权限
    details: 内置五种数据范围，靠 ORM 全局过滤器自动隔离。业务查询不用手写机构过滤条件。
  - icon: 🔭
    title: 有真实参考应用
    details: 在线演示跑的是独立开源的消费者应用 tenon-example：装包、加一个 CRM 模块、部署上线，全过程可复现。三个账号打开同一张列表看到 214、128、42 条，而查询代码里没有一行机构过滤。
    link: https://github.com/Tenon-Net/tenon-example
    linkText: 看看它怎么写的
  - icon: ⚡
    title: 零配置启动
    details: 默认 SQLite 自动建表、写种子，首次启动打印一次超管密码。换数据库只改一处配置。
  - icon: 📦
    title: 极简依赖
    details: 运行时只依赖 SqlSugar 和 Microsoft.* 官方库。Redis、对象存储这些按需引入。
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
    details: 新增实体、搭 CRUD、替换服务的流程都写成了标准 skills，AI 助手或开发者照着就能生成符合规范的代码。
    link: /zh/community/agent-skills
    linkText: 看看 Skills
