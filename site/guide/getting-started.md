# Getting Started

TenonAdmin is an admin/permission-management kernel built on ASP.NET Core, SqlSugar, Vue 3, Vite, and Naive UI. Instead of forking the whole project and building on top of it, it packages common capabilities — users, roles, menus, organizations, data permissions, logging — into modules that are **pluggable, replaceable, and extensible**.

## Try the sample first

The repo ships a minimal sample project. Clone it and run it directly:

```bash
dotnet run --project backend/samples/MinimalHost
```

On first startup it creates the database, seeds initial data, and prints a **randomly generated super-admin password** to the console — save it, since it's only printed once. It listens on `http://localhost:5100` by default.

## Three lines to integrate into an existing project

In an existing ASP.NET Core project, the core integration takes just three lines:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

Then call `app.Run()` as usual. On startup, automatic table creation, data seeding, JWT authentication, RBAC permissions, data scoping, and the admin API are all registered.

## Installation

The current version is **`0.1.0`**, published on nuget.org. In most cases, referencing the meta-package is enough to get the full backend:

```bash
dotnet add package TenonAdmin
```

If you need finer-grained control over dependencies, you can reference individual layers instead (see [Core Concepts · Package Layering](/guide/concepts#package-layering)).

## Frontend

The frontend is a Vue 3 + Naive UI admin template, located in the repo's `web/` directory:

```bash
cd web
npm install
npm run dev        # Vite runs on :5173, proxying /api and /openapi to the backend at :5100
```

## What's next

- Want to understand what "replaceable" really means and why the packages are layered this way → [Core Concepts](/guide/concepts)
- Want to deploy it to a server → [Deployment](/guide/deployment/)
- Want to build your own business logic on top of the kernel → [Adding a New Business Module](/guide/new-business/)
- Forked the repo to build on `web/` and want to pull in upstream updates → [Syncing Your Fork](/guide/sync-fork)

> The API may still change before 1.0; breaking changes are clearly marked in the [Changelog](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md). Development happens on the `dev` branch.
