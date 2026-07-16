import { defineConfig } from 'vitepress'

// TenonAdmin 文档门面站配置。双语:英文(默认,根路径)+ 简体中文(/zh/)。
// 自定义域 tenon.52moyu.net → base '/'。加语种只需在 locales 里再加一块 + 对应目录。

// ── English (default locale, root path) ──
const enGuideSidebar = [
  {
    text: 'Getting Started',
    items: [
      { text: 'Quick Start', link: '/guide/getting-started' },
      { text: 'Core Concepts', link: '/guide/concepts' },
    ],
  },
  {
    text: 'Advanced',
    items: [
      { text: 'Deployment', link: '/guide/deployment' },
      { text: 'New Business Module', link: '/guide/new-business' },
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
    { text: 'Internals', link: '/deep/architecture' },
    { text: 'Tutorials', link: '/tutorial/quickstart' },
    { text: 'Frontend', link: '/frontend/structure' },
    { text: 'Standards', link: '/standard/backend' },
    { text: 'Components', link: '/components/' },
    { text: 'Community', link: '/community/contributing' },
    { text: 'Live Demo', link: 'https://tenonadmin.52moyu.net/login' },
    { text: '0.1.0', link: 'https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md' },
  ],
  sidebar: {
    '/guide/': enGuideSidebar,
    '/faq': enGuideSidebar,
    '/deep/': [
      {
        text: 'Internals',
        items: [
          { text: 'Architecture & Package Layering', link: '/deep/architecture' },
          { text: 'Request Pipeline', link: '/deep/request-pipeline' },
          { text: 'Multi-Org Data Permissions', link: '/deep/data-scope' },
          { text: 'Auth & Security', link: '/deep/auth-security' },
          { text: 'Replaceability Model', link: '/deep/replaceability' },
          { text: 'Data Layer & Auditing', link: '/deep/data-layer' },
        ],
      },
    ],
    '/tutorial/': [
      {
        text: 'Tutorials',
        items: [
          { text: 'Your First Endpoint', link: '/tutorial/quickstart' },
          { text: 'Add a Business Module', link: '/tutorial/business-module' },
          { text: 'Add a Frontend Page', link: '/tutorial/frontend-page' },
          { text: 'Container Deployment', link: '/tutorial/docker-deploy' },
        ],
      },
    ],
    '/frontend/': [
      {
        text: 'Getting Started',
        items: [
          { text: 'Project Structure & Startup', link: '/frontend/structure' },
        ],
      },
      {
        text: 'Core',
        items: [
          {
            text: 'Routing & Dynamic Menus',
            link: '/frontend/routing/',
            items: [
              { text: 'Overview', link: '/frontend/routing/' },
              { text: 'Static Routes', link: '/frontend/routing/static' },
              { text: 'Dynamic Routes', link: '/frontend/routing/dynamic' },
              { text: 'Multi-App Portal', link: '/frontend/routing/portal' },
              { text: 'Router Guards', link: '/frontend/routing/guards' },
              { text: 'Keep-Alive & Named Pages', link: '/frontend/routing/keep-alive' },
            ],
          },
          {
            text: 'HTTP Request Layer',
            link: '/frontend/request/',
            items: [
              { text: 'Overview', link: '/frontend/request/' },
              { text: 'The Typed Client', link: '/frontend/request/client' },
              { text: 'Dev Proxy & CORS', link: '/frontend/request/proxy' },
              { text: 'Adapting to the Backend', link: '/frontend/request/backend' },
            ],
          },
          { text: 'Internationalization & Error Codes', link: '/frontend/i18n' },
          { text: 'Frontend Permissions', link: '/frontend/permission' },
        ],
      },
      {
        text: 'Appearance',
        items: [
          { text: 'Theme & Design Tokens', link: '/frontend/theme' },
          { text: 'Icons', link: '/frontend/icons' },
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
    text: '入门',
    items: [
      { text: '快速开始', link: '/zh/guide/getting-started' },
      { text: '核心概念', link: '/zh/guide/concepts' },
    ],
  },
  {
    text: '进阶',
    items: [
      { text: '部署', link: '/zh/guide/deployment' },
      { text: '新建业务模块', link: '/zh/guide/new-business' },
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
    { text: '深入原理', link: '/zh/deep/architecture' },
    { text: '实战教程', link: '/zh/tutorial/quickstart' },
    { text: '前端', link: '/zh/frontend/structure' },
    { text: '代码规范', link: '/zh/standard/backend' },
    { text: '组件生态', link: '/zh/components/' },
    { text: '参与', link: '/zh/community/contributing' },
    { text: '在线预览', link: 'https://tenonadmin.52moyu.net/login' },
    { text: '0.1.0', link: 'https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md' },
  ],
  sidebar: {
    '/zh/guide/': zhGuideSidebar,
    '/zh/faq': zhGuideSidebar,
    '/zh/deep/': [
      {
        text: '深入原理',
        items: [
          { text: '架构分层与包依赖', link: '/zh/deep/architecture' },
          { text: '请求管线', link: '/zh/deep/request-pipeline' },
          { text: '多组织数据权限', link: '/zh/deep/data-scope' },
          { text: '认证与安全', link: '/zh/deep/auth-security' },
          { text: '可替换性模型', link: '/zh/deep/replaceability' },
          { text: '数据层与审计', link: '/zh/deep/data-layer' },
        ],
      },
    ],
    '/zh/tutorial/': [
      {
        text: '实战教程',
        items: [
          { text: '从零跑通第一个接口', link: '/zh/tutorial/quickstart' },
          { text: '加一个业务模块', link: '/zh/tutorial/business-module' },
          { text: '加一个前端页面', link: '/zh/tutorial/frontend-page' },
          { text: '容器化部署', link: '/zh/tutorial/docker-deploy' },
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
        text: '核心',
        items: [
          {
            text: '路由与动态菜单',
            link: '/zh/frontend/routing/',
            items: [
              { text: '概览', link: '/zh/frontend/routing/' },
              { text: '静态路由', link: '/zh/frontend/routing/static' },
              { text: '动态路由', link: '/zh/frontend/routing/dynamic' },
              { text: '多应用门户', link: '/zh/frontend/routing/portal' },
              { text: '路由守卫', link: '/zh/frontend/routing/guards' },
              { text: 'Keep-Alive 与具名组件', link: '/zh/frontend/routing/keep-alive' },
            ],
          },
          {
            text: 'HTTP 请求层',
            link: '/zh/frontend/request/',
            items: [
              { text: '概览', link: '/zh/frontend/request/' },
              { text: '类型化客户端', link: '/zh/frontend/request/client' },
              { text: '开发代理与 CORS', link: '/zh/frontend/request/proxy' },
              { text: '对接后端响应', link: '/zh/frontend/request/backend' },
            ],
          },
          { text: '国际化与错误码', link: '/zh/frontend/i18n' },
          { text: '前端权限', link: '/zh/frontend/permission' },
        ],
      },
      {
        text: '外观',
        items: [
          { text: '主题与 tokens', link: '/zh/frontend/theme' },
          { text: '图标', link: '/zh/frontend/icons' },
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
        text: '组件生态',
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
