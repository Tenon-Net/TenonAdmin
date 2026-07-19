import { defineConfig } from 'vitepress'

// TenonAdmin 文档门面站配置。双语:英文(默认,根路径)+ 简体中文(/zh/)。
// 自定义域 tenon.52moyu.net → base '/'。加语种只需在 locales 里再加一块 + 对应目录。

// ── English (default locale, root path) ──
const enGuideSidebar = [
  {
    text: 'Get Started',
    items: [
      { text: 'Quick Start', link: '/guide/getting-started' },
      { text: 'Core Concepts', link: '/guide/concepts' },
    ],
  },
  {
    text: 'Build a Business Module',
    items: [
      { text: 'Add a Business Module (Backend)', link: '/guide/business-module' },
      { text: 'Add a Frontend Page', link: '/guide/frontend-page' },
    ],
  },
  {
    text: 'Customize the Kernel',
    items: [
      { text: 'Replace Built-in Services', link: '/guide/replace-service' },
      { text: 'Syncing Your Fork', link: '/guide/sync-fork' },
    ],
  },
  {
    text: 'Go Live',
    items: [
      { text: 'Security Baseline & Choosing a Route', link: '/guide/deployment/' },
      { text: 'Route A: Monolithic', link: '/guide/deployment/route-a' },
      { text: 'Route B: Reverse Proxy (nginx or Caddy)', link: '/guide/deployment/route-b' },
      { text: 'Route C: True Cross-Origin (CDN)', link: '/guide/deployment/route-c' },
      { text: 'Containers & Multi-Replica', link: '/guide/deployment/docker' },
    ],
  },
  {
    text: 'Help',
    items: [
      { text: 'FAQ', link: '/faq' },
    ],
  },
]

const enThemeConfig = {
  nav: [
    { text: 'Guide', link: '/guide/getting-started' },
    { text: 'Backend', link: '/backend/structure' },
    { text: 'Frontend', link: '/frontend/structure' },
    { text: 'Components', link: '/components/' },
    { text: 'Standards', link: '/standard/backend' },
    { text: 'Community', link: '/community/contributing' },
    { text: 'Live Demo', link: 'https://tenonadmin.52moyu.net/login' },
    { text: '0.1.0', link: 'https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md' },
  ],
  sidebar: {
    '/guide/': enGuideSidebar,
    '/faq': enGuideSidebar,
    '/backend/': [
      {
        text: 'Get Started',
        items: [
          { text: 'Project Structure & Startup', link: '/backend/structure' },
        ],
      },
      {
        text: 'Core Mechanisms',
        items: [
          { text: 'Architecture & Package Layering', link: '/backend/architecture' },
          { text: 'Request Pipeline', link: '/backend/request-pipeline' },
          { text: 'Multi-Org Data Permissions', link: '/backend/data-scope' },
          { text: 'Auth & Security', link: '/backend/auth-security' },
          { text: 'External Login (SSO)', link: '/backend/external-login' },
          { text: 'Realtime Notifications', link: '/backend/realtime' },
          { text: 'Data Layer & Auditing', link: '/backend/data-layer' },
        ],
      },
      {
        text: 'Extensibility',
        items: [
          { text: 'Replaceability Model', link: '/backend/replaceability' },
        ],
      },
    ],
    '/frontend/': [
      {
        text: 'Get Started',
        items: [
          { text: 'Project Structure & Startup', link: '/frontend/structure' },
        ],
      },
      {
        text: 'Routing & Menus',
        items: [
          { text: 'Routing & Dynamic Menus', link: '/frontend/routing' },
          { text: 'Multi-App Portal & Router Guards', link: '/frontend/portal-guards' },
        ],
      },
      {
        text: 'Requests & Contract',
        items: [
          { text: 'HTTP Request Layer', link: '/frontend/request' },
          { text: 'Backend Contract & Error Codes', link: '/frontend/api-contract' },
        ],
      },
      {
        text: 'Features',
        items: [
          { text: 'Frontend Permissions', link: '/frontend/permission' },
          { text: 'Internationalization', link: '/frontend/i18n' },
        ],
      },
      {
        text: 'Appearance',
        items: [
          { text: 'Theme & Icons', link: '/frontend/appearance' },
        ],
      },
    ],
    '/standard/': [
      {
        text: 'Code Standards',
        items: [
          { text: 'Backend', link: '/standard/backend' },
          { text: 'Frontend', link: '/standard/frontend' },
          { text: 'Commits', link: '/standard/commit' },
        ],
      },
    ],
    '/components/': [
      {
        text: 'Components',
        items: [
          { text: 'Overview', link: '/components/' },
          { text: 'ProTable — column-driven table', link: '/components/pro-table' },
          { text: 'IconPicker — icon selector', link: '/components/icon-picker' },
        ],
      },
    ],
    '/community/': [
      {
        text: 'Community',
        items: [
          { text: 'Contributing', link: '/community/contributing' },
          { text: 'Agent Skills & AI-Assisted Dev', link: '/community/agent-skills' },
        ],
      },
    ],
  },
  editLink: {
    pattern: 'https://github.com/Tenon-Net/TenonAdmin/edit/main/site/:path',
    text: 'Edit this page on GitHub',
  },
  footer: {
    message: 'Released under the Apache License 2.0',
    copyright: 'Copyright © 2025 TenonAdmin',
  },
}

// ── 简体中文 (/zh/) ──
const zhGuideSidebar = [
  {
    text: '上手',
    items: [
      { text: '快速开始', link: '/zh/guide/getting-started' },
      { text: '核心概念', link: '/zh/guide/concepts' },
    ],
  },
  {
    text: '开发业务模块',
    items: [
      { text: '加一个业务模块(后端)', link: '/zh/guide/business-module' },
      { text: '加一个前端页面', link: '/zh/guide/frontend-page' },
    ],
  },
  {
    text: '定制内核',
    items: [
      { text: '替换内置服务', link: '/zh/guide/replace-service' },
      { text: '同步上游 Fork', link: '/zh/guide/sync-fork' },
    ],
  },
  {
    text: '上线',
    items: [
      { text: '安全基线与选路线', link: '/zh/guide/deployment/' },
      { text: '路线 A:单体部署', link: '/zh/guide/deployment/route-a' },
      { text: '路线 B:反向代理(nginx 或 Caddy)', link: '/zh/guide/deployment/route-b' },
      { text: '路线 C:真跨源(CDN)', link: '/zh/guide/deployment/route-c' },
      { text: '容器化与多副本', link: '/zh/guide/deployment/docker' },
    ],
  },
  {
    text: '帮助',
    items: [
      { text: '常见问题', link: '/zh/faq' },
    ],
  },
]

const zhThemeConfig = {
  nav: [
    { text: '指南', link: '/zh/guide/getting-started' },
    { text: '后端', link: '/zh/backend/structure' },
    { text: '前端', link: '/zh/frontend/structure' },
    { text: '组件', link: '/zh/components/' },
    { text: '规范', link: '/zh/standard/backend' },
    { text: '参与', link: '/zh/community/contributing' },
    { text: '在线预览', link: 'https://tenonadmin.52moyu.net/login' },
    { text: '0.1.0', link: 'https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md' },
  ],
  sidebar: {
    '/zh/guide/': zhGuideSidebar,
    '/zh/faq': zhGuideSidebar,
    '/zh/backend/': [
      {
        text: '入门',
        items: [
          { text: '项目结构与启动', link: '/zh/backend/structure' },
        ],
      },
      {
        text: '核心机制',
        items: [
          { text: '架构分层与包依赖', link: '/zh/backend/architecture' },
          { text: '请求管线', link: '/zh/backend/request-pipeline' },
          { text: '多组织数据权限', link: '/zh/backend/data-scope' },
          { text: '认证与安全', link: '/zh/backend/auth-security' },
          { text: '外部登录（SSO）', link: '/zh/backend/external-login' },
          { text: '实时通知', link: '/zh/backend/realtime' },
          { text: '数据层与审计', link: '/zh/backend/data-layer' },
        ],
      },
      {
        text: '扩展',
        items: [
          { text: '可替换性模型', link: '/zh/backend/replaceability' },
        ],
      },
    ],
    '/zh/frontend/': [
      {
        text: '入门',
        items: [
          { text: '项目结构与启动', link: '/zh/frontend/structure' },
        ],
      },
      {
        text: '路由与菜单',
        items: [
          { text: '路由与动态菜单', link: '/zh/frontend/routing' },
          { text: '多应用门户与路由守卫', link: '/zh/frontend/portal-guards' },
        ],
      },
      {
        text: '请求与契约',
        items: [
          { text: 'HTTP 请求层', link: '/zh/frontend/request' },
          { text: '对接后端:响应契约与错误码', link: '/zh/frontend/api-contract' },
        ],
      },
      {
        text: '功能',
        items: [
          { text: '前端权限', link: '/zh/frontend/permission' },
          { text: '国际化', link: '/zh/frontend/i18n' },
        ],
      },
      {
        text: '外观',
        items: [
          { text: '主题与图标', link: '/zh/frontend/appearance' },
        ],
      },
    ],
    '/zh/standard/': [
      {
        text: '代码规范',
        items: [
          { text: '后端规范', link: '/zh/standard/backend' },
          { text: '前端规范', link: '/zh/standard/frontend' },
          { text: '提交规范', link: '/zh/standard/commit' },
        ],
      },
    ],
    '/zh/components/': [
      {
        text: '组件',
        items: [
          { text: '概览', link: '/zh/components/' },
          { text: 'ProTable — 列驱动表格', link: '/zh/components/pro-table' },
          { text: 'IconPicker — 图标选择器', link: '/zh/components/icon-picker' },
        ],
      },
    ],
    '/zh/community/': [
      {
        text: '参与',
        items: [
          { text: '贡献指南', link: '/zh/community/contributing' },
          { text: 'Agent Skills 与 AI 辅助开发', link: '/zh/community/agent-skills' },
        ],
      },
    ],
  },
  editLink: {
    pattern: 'https://github.com/Tenon-Net/TenonAdmin/edit/main/site/:path',
    text: '在 GitHub 上编辑本页',
  },
  footer: {
    message: '基于 Apache License 2.0 开源',
    copyright: 'Copyright © 2025 TenonAdmin',
  },
  docFooter: {
    prev: '上一页',
    next: '下一页',
  },
  outline: { label: '本页目录' },
  lastUpdatedText: '最后更新',
  returnToTopLabel: '返回顶部',
  darkModeSwitchLabel: '外观',
  sidebarMenuLabel: '菜单',
}

export default defineConfig({
  base: '/',
  title: 'TenonAdmin',
  lastUpdated: true,
  cleanUrls: true,
  head: [
    ['link', { rel: 'icon', href: '/icon-128.png' }],
  ],
  themeConfig: {
    logo: '/icon-128.png',
    socialLinks: [
      { icon: 'github', link: 'https://github.com/Tenon-Net/TenonAdmin' },
    ],
    search: {
      provider: 'local',
    },
  },
  locales: {
    root: {
      label: 'English',
      lang: 'en',
      description: 'A zero-config, extensible RBAC admin-system kernel for ASP.NET Core — three lines to integrate.',
      themeConfig: enThemeConfig,
    },
    zh: {
      label: '简体中文',
      lang: 'zh-CN',
      link: '/zh/',
      description: '三行代码,为 ASP.NET Core 项目接入一套完整、可扩展的 RBAC 权限管理。',
      themeConfig: zhThemeConfig,
    },
  },
})
