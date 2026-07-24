<!-- Keep in sync with README.zh-CN.md (canonical) -->

English | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

<p align="center">
  <img src="web/design-mockups/brand/icon-128.png" width="80" height="80" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center">
  <em>Three lines of code to add a complete, extensible RBAC access-management layer to your ASP.NET Core project.</em>
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
  <a href="https://tenonadmin.52moyu.net/login"><strong>🔗 Live Demo</strong></a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://tenon.52moyu.net"><strong>📖 Docs</strong></a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="CHANGELOG.md"><strong>📋 Changelog</strong></a>
</p>

---

## 🎨 What is this?

TenonAdmin packages the common back-office machinery as NuGet packages. Users, roles, menus, multi-org data permissions, dictionaries, config, operation logs, file uploads — everything every back office ends up rebuilding — comes in via `dotnet add package`. Three lines in `Program.cs` give you a complete admin API:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

- **Runs by default** — zero-config start: tables auto-created, seed data loaded, SQLite as the fallback. First run doesn't even need a database server.
- **Replace what you don't like** — every built-in service is interface-based and registered with `TryAdd`. Register your own implementation and the built-in one steps aside. No forking.
- **Upgrading = bumping a package version** — bug fixes and new features arrive as package updates; your business code doesn't move.

The usual approach is cloning a template repo: hundreds of files become yours to maintain, business code and framework code end up tangled together, and when the framework ships a new version you're stuck merging diffs by hand. TenonAdmin flips that — the common machinery is a package dependency, and your business code stays your business code.

The frontend is covered too: **two feature-equivalent templates** (Vue and React). Pick whichever feels right and use it as the starting point of your own project.

## 🚀 Quick Start

### Requirements

- .NET 10 SDK
- Node.js 20+ (only if you run a frontend template)

### Take it for a spin

Clone the repo, then one command for the backend:

```bash
dotnet run --project backend/samples/MinimalHost
```

First startup creates the database and tables, loads seed data, and prints a randomly generated super-admin password to the console (account `superAdmin`). API is up at http://localhost:5100.

Pick a frontend (or run both — the ports don't clash):

```bash
cd web && npm install && npm run dev            # Vue → http://localhost:5173
cd web-react && npm install && npm run dev      # React → http://localhost:5174
```

Open the browser, log in with the credentials from the console, and there's your full back office. On Windows it's even lazier: double-click `dev.bat` in the repo root and the backend plus both frontends start in one go.

### Plug it into your own project

```bash
dotnet add package TenonAdmin
```

Add the three lines above to `Program.cs`, and JWT auth, RBAC, data permissions, and every management endpoint register themselves on startup. Different database? One config block:

```jsonc
// appsettings.json
"TenonAdmin": {
  "Database": {
    "DbType": "MySql",          // Sqlite / MySql / SqlServer / PostgreSQL
    "ConnectionString": "..."
  }
}
```

### Don't like a built-in implementation? Swap it

Every built-in service is interface-based and registered with `TryAdd` — register yours first and the built-in one steps aside:

```csharp
// e.g. swap the password hashing algorithm: register yours before AddTenonAdmin
builder.Services.AddSingleton<IPasswordHasher, MyPasswordHasher>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

It goes finer than that: long service methods are split into small `virtual` steps, so you can subclass a built-in service and override just the one step you care about instead of copying the whole method. And this replaceability isn't a slogan — a dedicated set of contract tests locks it in place.

## ✨ Backend features

- **Auth** — Account/password + captcha, JWT + refresh-token rotation, login lockout, online sessions & force-logout
- **RBAC** — Roles, three-level menus (directory / page / button), button-level permission codes, role-menu authorization
- **Data permissions** — All / this org / org & children / self only / custom orgs, enforced by ORM global filters — zero filtering code in your business logic
- **Multi-app portal** — App management, independent menu trees, app selection & switching
- **Organization** — Org tree, positions, multi-role users with a primary org
- **Notifications** — In-app notices & announcements, targetable to everyone / roles / users
- **Dictionary & config** — Dict types + items + key-value config, cached with event-driven invalidation
- **Logging** — Auto-recorded operation logs with sensitive-input masking
- **File management** — Upload/download, size limits, extension whitelist, path-traversal protection
- **Multi-database** — SQLite (default) / MySQL / SQL Server / PostgreSQL; switching is a config change
- **Multi-replica** — Optional Redis cache, cross-replica rate-limit counters, per-replica snowflake worker IDs — scales out without surprises
- **Restrained dependencies** — Core packages depend only on SqlSugarCore + Microsoft.* at runtime; no third-party framework zoo dumped into your project

## 🖥️ Frontend: two official templates, pick one

The same backend contract ships with two fully independent frontend templates — take whichever stack you're comfortable with:

| | `web/` | `web-react/` |
|---|---|---|
| Framework | Vue 3 + Naive UI | React 19 + Ant Design 6 |
| State / routing | Pinia + vue-router | zustand + react-router |
| i18n | vue-i18n | react-i18next |
| Dev port | :5173 | :5174 |

Zero sharing is deliberate: the two templates never import from each other — not even a utility function. Take one and you only carry that one's dependencies; delete the other and nothing happens. Features were ported page by page, so both sides have:

- **Contract-generated API** — OpenAPI → `schema.d.ts`, type-safe end to end; change an endpoint and the frontend fails to compile
- **Dynamic routing** — Backend menu tree drives route registration; multi-app portal with seamless switching
- **Button-level permissions** — `v-auth` directive in Vue, `<Can>` component in React, same permission codes
- **Column-driven tables** — One `columns` array drives the search form, dict rendering, and column settings
- **Design tokens + light/dark themes** — Four-layer CSS variable tokens, follows the system or toggles manually
- **Three login-page skins** — Switchable out of the box, style-isolated
- **In-house component library** — FormContainer (modal/drawer two-in-one), StatusSwitch (pessimistic-update toggle), dict suite, OrgTreeSelect, FileUpload (chunked / resumable / instant), PasswordStrength, chart wrappers, and more — implemented once per template

## 🧩 Repository layout

| Directory | What it is |
|---|---|
| `backend/` | .NET 10 kernel (5 NuGet packages) + sample host + tests |
| `web/` | Vue 3 + Naive UI frontend template, self-contained |
| `web-react/` | React 19 + Ant Design 6 frontend template, self-contained |
| `templates/` | `dotnet new tenon-app` project template |
| `site/` | Docs site source (VitePress, zh/en) |
| `docs/` | Design docs and development records |

## 📋 Project status

**The API may still change before 1.0** — breaking changes are called out in the [changelog](CHANGELOG.md). Development happens on the `dev` branch; issues and PRs welcome.

## 📄 License

[Apache License 2.0](LICENSE)
