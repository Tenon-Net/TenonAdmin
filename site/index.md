---
layout: home

hero:
  name: TenonAdmin
  text: Full RBAC in three lines of code
  tagline: A zero-config, extensible ASP.NET Core admin-system kernel with built-in RBAC and data permissions
  image:
    src: /icon-128.png
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
    details: Every built-in service is interface-registered and overridable step by step — swap any piece without forking, upgrade without conflicts.
  - icon: 🏢
    title: Multi-Org Data Permissions
    details: Five built-in data scopes, enforced automatically via ORM global filters — business queries never need manual org-filter conditions.
  - icon: ⚡
    title: Zero-Config Startup
    details: SQLite by default auto-creates tables and writes seed data, printing the super-admin password once on first startup; switching databases is a single config change.
  - icon: 📦
    title: Minimal Dependencies
    details: Runtime depends only on SqlSugar and Microsoft.* official libraries — Redis, object storage, etc. are opt-in.
  - icon: 🔐
    title: Auth & Security
    details: JWT authentication, CAPTCHA, login lockout, forced logout, and log redaction all included — a secure baseline out of the box.
  - icon: 🖥️
    title: Full-Stack Delivery
    details: Ships with a Vue 3 + Naive UI admin console, with containerized deployment and multi-replica horizontal scaling supported.
  - icon: 🧰
    title: Component Ecosystem
    details: Shared components like ProTable and IconPicker are published as standalone npm packages — install them individually into any Vue 3 + Naive UI project.
    link: /components/
    linkText: Browse the components
  - icon: 🤖
    title: Assisted-Development Skills
    details: Workflows like adding entities, scaffolding CRUD, and swapping services are written up as standard skills — AI assistants or developers follow them to generate standards-compliant code.
    link: /community/agent-skills
    linkText: Browse the skills
