---
layout: home

hero:
  name: TenonAdmin
  text: Admin features for ASP.NET Core
  tagline: Install accounts, roles, and data permissions through NuGet; build business features in your project with Vue or React
  image:
    src: /tenon-mark.png
    alt: TenonAdmin
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: Live Demo
      link: https://tenonadmin.52moyu.net/login
    - theme: alt
      text: GitHub
      link: https://github.com/Tenon-Net/TenonAdmin

features:
  - icon: 🧩
    title: Pluggable Architecture
    details: Customize sign-in or file storage through interfaces and overridable methods. Keep business code separate and check compatibility against release notes when upgrading.
  - icon: 🏢
    title: Multi-Org Data Permissions
    details: Let employees see their own customers and managers see their department’s records. Supported queries apply data-scope filters; raw SQL needs its own permission checks.
  - icon: 🔀
    title: Approval Workflows
    details: An optional package adds workflow design, submission, and approval, with pages in both Vue and React templates.
    link: /guide/workflow
    linkText: Run your first approval
  - icon: 🔗
    title: System Integration
    details: Register external applications, grant API access, and track outbound delivery through delivery tasks.
    link: /guide/integration
    linkText: Connect an external system
  - icon: 🔭
    title: A Real Reference App
    details: The separate tenon-example app shows package installation, CRM development, and deployment. Follow its entities, endpoints, and pages to learn how a complete feature fits together.
    link: https://github.com/Tenon-Net/tenon-example
    linkText: See how it's written
  - icon: ⚡
    title: Zero-Config Startup
    details: The local sample uses SQLite and creates tables, menus, and an administrator account on first startup. Try the admin interface without installing a database server.
  - icon: 📦
    title: Minimal Dependencies
    details: Core runtime dependencies are limited to SqlSugar and Microsoft.*. Add optional packages such as Excel and workflows when your application needs them.
  - icon: 🔐
    title: Auth & Security
    details: JWT auth, login lockout, rate limiting, forced logout, and log redaction are on by default; three CAPTCHA styles ship built in and switch on when you want them.
  - icon: 🖥️
    title: Full-Stack Delivery
    details: Ships with two self-contained admin console templates — Vue 3 + Naive UI or React 19 + Ant Design, pick one — with containerized deployment and multi-replica horizontal scaling supported.
  - icon: 🧰
    title: Component Ecosystem
    details: Shared components like ProTable and IconPicker are published as standalone npm packages — install them individually into any Vue 3 + Naive UI project.
    link: /components/
    linkText: Browse the components
  - icon: 🤖
    title: Assisted-Development Skills
    details: Development Skills provide steps and checks for entities, endpoints, pages, and service extensions. Developers and AI assistants can use them as references; verify business rules and permissions after generation.
    link: /community/agent-skills
    linkText: Browse the skills
---

## Start with your goal

| What you want to do | Recommended starting point |
|---|---|
| Try the admin interface | [Quick Start](/guide/getting-started): run the sample and sign in |
| Evaluate it for your project | [Core Concepts](/guide/concepts): understand framework and business responsibilities |
| Build a first feature | [Business Module](/guide/business-module) → [Frontend Page](/guide/frontend-page) |
| Customize a default implementation | [Replace Built-in Services](/guide/replace-service) |
| Deploy or upgrade | [Deployment](/guide/deployment/) and the [Changelog](/changelog) |

For frontend work, start with [Vue](/frontend/getting-started) or [React](/frontend-react/getting-started): build a small page using an existing API, then connect menus and permissions. To connect an external business system, start with [System Integration](/guide/integration).
