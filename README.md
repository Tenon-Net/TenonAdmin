<!-- Keep in sync with README.zh-CN.md (canonical) -->

English | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

<p align="center">
  <img src="web/design-mockups/brand/tenon-logo.svg" width="80" height="80" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center">
  <em>Three lines of code to add a complete, extensible RBAC access-management layer to your ASP.NET Core project.</em>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="License"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/stargazers"><img src="https://img.shields.io/github/stars/Tenon-Net/TenonAdmin" alt="Stars"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/network/members"><img src="https://img.shields.io/github/forks/Tenon-Net/TenonAdmin" alt="Forks"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>

---

TenonAdmin is an admin access-management system built on ASP.NET Core, SqlSugar, Vue 3, Vite, and Naive UI.

It works a little differently from a typical admin template. Instead of asking you to copy an entire project and build on top of it, TenonAdmin packages the common capabilities — users, roles, menus, organizations, data permissions, logging — into modules you can plug in, replace, and extend.

In an existing ASP.NET Core project, service registration, app build, and endpoint mapping are all it takes to bring in a baseline of login authentication, RBAC, and a management UI. You can use the default implementations as-is, or replace the services and workflows to fit your own business.

## Why TenonAdmin

When you actually build admin systems, capabilities like users, roles, menus, permissions, organizations, and logging tend to get re-implemented over and over.

Copying a whole admin template gets you started quickly, but as business code accumulates the project usually ends up tightly coupled to the template itself. Later on, upgrading the base capabilities, syncing upstream changes, or replacing just one part becomes awkward.

TenonAdmin aims to pull these common capabilities out of the specific business logic, so an access-management backend can be used directly and also plugged into an existing project fairly naturally.

- **Three-line integration** — Register and map in an existing ASP.NET Core project to enable login authentication, RBAC, and the management API.
- **Runs on defaults** — Uses SQLite by default; creates tables and seeds initial data automatically on startup.
- **Replaceable on demand** — Core services are registered behind interfaces; key steps in the default implementations support inheritance and override.
- **Dependencies on demand** — Only SqlSugar + `Microsoft.*` at runtime; heavier capabilities are split into optional packages you add when you need them.
- **Built-in data permissions** — Five data scopes: all data, this org, this org & children, self only, and custom orgs.
- **Full stack, front and back** — The backend provides the permission and business foundation; the frontend ships a management UI built on Vue 3 and Naive UI.

## Quick Start

Run the sample project included in the repo:

```bash
dotnet run --project backend/samples/MinimalHost
```

On first startup it creates the database, seeds initial data, and prints a randomly generated super-admin password to the console — make sure to save it.

In an existing ASP.NET Core project, the core integration is just three lines:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

Then start the app the usual way with `app.Run()`.

Once running, auto table creation, data seeding, JWT authentication, RBAC, data permissions, and the admin management API are all registered.

## Features

- **Authentication** — Account/password login, image captcha, JWT, refresh-token rotation, login lockout, online sessions, and force-logout
- **RBAC** — Role management, three-level menus (directory / page / button), button-level permission codes, and role-menu authorization
- **Multi-app portal** — App/module management, per-app menu trees, app selection and switching, per-user default app
- **Data permissions** — Five data scopes, applied automatically at query time via global ORM filters
- **Organization** — Users, org tree, and positions; a user can hold multiple roles and have a primary org
- **Notifications** — In-app notices and announcements, sendable to everyone / specific roles / specific users, with an unread badge and a message panel in the header
- **Dictionary & config** — Dict types, dict items, and key-value config, with caching and event-driven cache invalidation
- **Logging** — Operation logs recorded automatically with sensitive input masked, plus login IP, User-Agent, and result
- **File management** — Local file upload/download, size limits, extension whitelist, and path-traversal protection
- **Personal center** — Change password, edit profile, and set avatar

The frontend uses Vue 3 and Naive UI, and currently ships three switchable login-page skins.

## Customization

TenonAdmin offers several levels of extension; pick one based on how deep your change goes:

1. **Change config** — Adjust the `TenonAdmin` section in `appsettings.json`
2. **Replace a service** — Register a custom implementation to replace a built-in default
3. **Inherit a default** — Subclass an existing service and override only the workflow steps you need
4. **Extend endpoints** — Replace default routes or add your own business APIs

Entity extension and custom business modules are supported too, so you can keep building real business on top of the existing permission system.

## Tech Stack

### Backend

- .NET 10 (ASP.NET Core)
- SqlSugar ORM
- JWT Bearer authentication
- SQLite (default) / MySQL / SQL Server / PostgreSQL

### Frontend

- Vue 3.5 + TypeScript 5.7
- Naive UI 2.41
- Pinia 3 (persisted)
- Vue Router 4
- Vue I18n
- Vite 6
- ECharts 5.6

### NuGet packages

```text
TenonAdmin.Core → TenonAdmin.SqlSugar → TenonAdmin.Services → TenonAdmin.AspNetCore → TenonAdmin
```

In most cases, referencing `TenonAdmin` gives you the full backend; when you need finer-grained dependency control, you can reference an individual layer instead.

## Project Status

Current version **`0.1.0`**, published on nuget.org:

```bash
dotnet add package TenonAdmin
```

The backend kernel, full management UI, config center, containerized delivery, and multi-replica support (Redis cache, cross-replica shared rate-limit counters, per-replica snowflake worker ids) are all working and covered by CI.

**The API may still change before 1.0** — breaking changes are called out in the [changelog](CHANGELOG.md). Development happens on the `dev` branch.

## Project Structure

```text
tenon-admin/
├── backend/
│   ├── src/
│   │   ├── TenonAdmin.Core/            # Contracts: interfaces, Options, ErrorCode
│   │   ├── TenonAdmin.SqlSugar/        # Data: ORM, Repository, CodeFirst
│   │   ├── TenonAdmin.Services/        # Domain: entities, services, RBAC
│   │   ├── TenonAdmin.AspNetCore/      # ASP.NET Core integration: controllers, filters, JWT
│   │   ├── TenonAdmin/                 # Full backend meta-package
│   │   └── TenonAdmin.Caching.Redis/   # Optional Redis cache
│   ├── samples/MinimalHost/            # Minimal sample project
│   └── tests/                          # xUnit tests
├── web/                                # Vue admin frontend
└── docs/                               # Docs and roadmap
```

## Documentation

- [Business module guide](docs/new-business-guide.md)
- [Deployment](docs/deployment.md)
- [Architecture & design](docs/rebuild-design.md)
- [Roadmap](docs/dev-plan.md)

## License

Released under the [Apache License 2.0](LICENSE).
