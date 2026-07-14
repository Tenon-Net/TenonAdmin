English | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

<p align="center">
  <img src="web/design-mockups/brand/tenon-logo.svg" width="80" height="80" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center">
  <em>One NuGet package, three lines of code — a full enterprise admin system kernel.</em>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="License"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/stargazers"><img src="https://img.shields.io/github/stars/Tenon-Net/TenonAdmin" alt="Stars"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/network/members"><img src="https://img.shields.io/github/forks/Tenon-Net/TenonAdmin" alt="Forks"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>

---

TenonAdmin is an admin template built on ASP.NET Core + SqlSugar + Vue 3 + Vite + Naive UI. Out of the box you get login, RBAC, multi-org data permissions, and a full management UI — and every piece can be swapped out. If you're building an internal admin system and don't want to start from users-and-permissions boilerplate every time, just use this. It runs standalone or plugs into an existing ASP.NET Core project.

## Why This Exists

Most .NET admin frameworks give you a running app but lock you in. Fork the repo, fight the upgrades, carry deps you didn't ask for. TenonAdmin takes a different approach — it ships as a NuGet package. Your project references it, not the other way around.

- **Three-line onboarding** — `AddTenonAdmin()` + `MapTenonAdmin()` and you have a full backend with auth, RBAC, and a management UI. No scaffolding to copy.
- **Zero config to start** — Default SQLite, auto table creation, random superadmin password on first boot. `dotnet run` and log in.
- **Everything is replaceable** — Services are interface-backed, methods are `virtual`, DI uses `TryAdd`. Override one step in a workflow without copying the whole method. Four layers: config → service replacement → inheritance → endpoint override.
- **No dependency bloat** — Only SqlSugar + `Microsoft.*` at runtime. Anything heavier lives in an optional package you pull when you actually need it — today that's `TenonAdmin.Caching.Redis` (required for multi-replica deployments).
- **Multi-org data permissions** — The one most admin frameworks skip. Five scope types (all / this org / this org & children / self only / custom), configured per role, enforced automatically at the ORM query level via global filters.

## Quick Start

Run the included sample:

```bash
dotnet run --project backend/samples/MinimalHost
# First boot prints a random superadmin password — don't miss it
```

In your own project, three lines in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

Auto DDL, seed data, JWT auth, RBAC — all wired. See the [business module guide](docs/new-business-guide.md) for adding your own entities and controllers.

## Features

- **Authentication** — Username/password + captcha, JWT + refresh token rotation, login lockout, online sessions with force-logout
- **RBAC** — Roles, three-level menus (directory / page / button), button-level permissions, role-menu authorization
- **Multi-app portal** — Module management, per-app menu trees, app picker at login, per-user default app
- **Multi-org data permissions** — Five scope types, per-role config, automatic query-level filtering via global ORM filters
- **Organization** — Users, org tree, positions; multi-role per user, primary org
- **Dictionary & config** — Dict types + items, key-value system config with cache and event-driven invalidation
- **Logging** — Auto-recorded operation logs (with input masking), login logs with IP / UA / result
- **File management** — Local upload/download, size limits, extension whitelist, path traversal protection
- **Personal center** — Password change, profile editing, avatar

Frontend: Vue 3 + Naive UI admin template with three swappable login page skins.

## Customization

Four layers, by increasing depth:

1. **Config** — Override the `TenonAdmin` section in `appsettings.json`
2. **Service replacement** — Register your own implementation for any built-in interface (yours wins via `TryAdd`)
3. **Inheritance** — Subclass a default service, override just the template-method step you need
4. **Endpoint override** — Replace or extend built-in controller routes

Also supports entity extension and custom business modules — [details](docs/rebuild-design.md).

## Tech Stack

**Backend**

- .NET 10 (ASP.NET Core)
- SqlSugar ORM
- JWT Bearer authentication
- Snowflake ID generation
- OpenAPI (frontend contract source)
- SQLite (default) / MySQL / SQL Server / PostgreSQL

**Frontend**

- Vue 3.5 + TypeScript 5.7
- Naive UI 2.41
- Pinia 3 (persisted)
- Vue Router 4 · Vue I18n
- Vite 6
- ECharts 5.6
- openapi-fetch (contract-generated API client)
- OxLint

**NuGet packages**

```
TenonAdmin.Core → TenonAdmin.SqlSugar → TenonAdmin.Services → TenonAdmin.AspNetCore → TenonAdmin
```

Install `TenonAdmin` for the whole stack, or reference individual layers for finer control.

## Project Status

Current version **`0.1.0`**, published on nuget.org:

```bash
dotnet add package TenonAdmin
```

Backend kernel, full admin UI, config center, containerized delivery, and multi-replica support (Redis-backed cache, shared rate-limit counters, per-replica snowflake worker ids) are all working and covered by CI.

**The API may still change before 1.0** — breaking changes are called out in the [changelog](CHANGELOG.md). Development happens on the `dev` branch.

## Project Structure

```
tenon-admin/
├── backend/
│   ├── src/
│   │   ├── TenonAdmin.Core/            # Contracts: interfaces, Options, ErrorCode
│   │   ├── TenonAdmin.SqlSugar/        # Data: ORM, Repository, CodeFirst
│   │   ├── TenonAdmin.Services/        # Domain: entities, services, RBAC
│   │   ├── TenonAdmin.AspNetCore/      # Host: controllers, filters, JWT
│   │   ├── TenonAdmin/                 # Meta-package (install this one)
│   │   └── TenonAdmin.Caching.Redis/   # Optional: Redis cache
│   ├── samples/MinimalHost/            # Sample project (three-line startup)
│   └── tests/                          # xUnit tests
├── web/                                # Vue admin frontend
└── docs/                               # Design docs, guides, roadmap
```

## Documentation

- [Getting started with business modules](docs/new-business-guide.md)
- [Deployment](docs/deployment.md)
- [Architecture & design](docs/rebuild-design.md)
- [Roadmap](docs/dev-plan.md)

## License

[Apache-2.0](LICENSE)
