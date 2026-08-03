# Quick Start

That database you were about to go install: you don't need it. Clone the repo, `dotnet run`, and the console prints a super-admin password you can log straight in with. The SQLite file, the tables, and the seed data all get created on the kernel's first startup, with not a line of configuration written.

::: tip Prerequisites
- .NET 10 SDK
- Node.js (20+ recommended), if you also want to run the frontend
:::

## Run the sample first

The repo ships a minimal sample host, `backend/samples/MinimalHost`, whose `Program.cs` is only a few lines of wiring. After cloning the repo, just run it:

```bash
dotnet run --project backend/samples/MinimalHost
```

On first startup it does three things automatically: creates tables via the default SQLite (CodeFirst — tables are generated from the entity classes, no hand-written DDL; the database file lands under `backend/samples/MinimalHost/data/`), writes seed data (menus, roles, the super-admin account), and then listens on `http://localhost:5100` (that port is hard-coded in `launchSettings.json`, dodging the 5000 that AirPlay squats on macOS).

To start backend and frontend together locally, use `dev.bat` at the repo root — it brings up MinimalHost and the frontend Vite in two separate windows (installing frontend dependencies on the first run); `stop.bat` stops them.

## Verify the three probes

Once the service is up, confirm all three endpoints respond:

```bash
# Liveness probe — only checks the process is up, touches no dependencies
curl http://localhost:5100/health

# Readiness probe: returns Healthy only if both DB and cache are reachable
curl http://localhost:5100/health/ready

# OpenAPI contract, mounted only in Development, the source for the frontend's gen:api
curl http://localhost:5100/openapi/v1.json
```

The first two should return `Healthy`. `/openapi/v1.json` returns a big blob of JSON that you'll use later to generate the frontend types; this endpoint isn't mounted in production, so a 404 against it there is expected behavior, not a missing setting.

## Log in and call your first endpoint

`GET /api/v1/ping` is the smallest protected endpoint in the kernel — it only lets you through with a valid token. Which raises the first question: where does that password come from?

The seed runs once, only when the `sys_user` table is empty. Running MinimalHost is a zero-config startup with no password configured, so the kernel generates a 16-character random password (from a cryptographically secure source, with easily-confused characters like `0/O` and `1/l/I` stripped out) and prints it inside a prominent box in the console log of the startup that **creates the account** — that once, and never again. The banner itself is hard-coded Chinese in the kernel:

```text
╔══════════════════════════════════════════════════════╗
║  TenonAdmin 首次启动,已创建超级管理员                  ║
║  账号: superAdmin
║  密码: xxxxxxxxxxxxxxxx
║  此密码仅本次显示,请登录后立即修改!                    ║
╚══════════════════════════════════════════════════════╝
```

The account is always `superAdmin`; copy that password string down.

::: warning The random password is printed only once
Didn't catch it? Don't panic — in a local experimentation environment, delete the database file under `backend/samples/MinimalHost/data` and `dotnet run` again; an empty database reseeds. You can't wipe a production database like that — there you either configure a fixed password up front (see below) or change the password immediately after logging in.
:::

Want a fixed password you control (shared across a team, CI, repeated wipe-and-reseed)? Copy `backend/samples/MinimalHost/appsettings.Development.json.example` to `appsettings.Development.json` and fill in `Seed:AdminPassword`:

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "your-password" } } }
```

This file is excluded by `.gitignore` (it holds local credentials) and won't enter version control. With it set, the startup log no longer prints a random password, and you just log in with the account and password you chose. Note that the seed only recognizes an empty database: once any user exists, changing this won't overwrite the existing account — to reset, you have to wipe the database and start over.

The image captcha is off by default (`Security:Captcha:Enabled` defaults to off), so login only needs an account and password:

```bash
curl -X POST http://localhost:5100/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"account":"superAdmin","password":"<the password from above>"}'
```

`data.accessToken` in the response envelope is your token:

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": false } }
```

The super-admin seed doesn't force a password change on first login, so `mustChangePassword` is `false` (only a regular user created by an admin, or whose password was reset, gets `true`, and the frontend uses that to force a redirect to the change-password page). Attach the token and call ping:

```bash
curl http://localhost:5100/api/v1/ping \
  -H "Authorization: Bearer <accessToken>"
```

Response:

```json
{ "code": 0, "data": { "pong": true, "account": "superAdmin", "at": "2026-07-...T..." } }
```

Without a token — or with an expired or revoked one — you get a `401` (standard envelope, `code=40006`). The super admin (the `sadm` claim in the token) automatically bypasses the subsequent `[RolePermission]` permission-code check; a regular user first has to attach the matching route in menu management and grant it in role management before they can call the same endpoint — the full write-up of that chain is in [Add a Business Module](/guide/business-module).

## Integrate into your own project in three lines

What you ran above is the sample bundled with the repo. To actually wire the kernel into your own ASP.NET Core project, first install the meta-package:

```bash
dotnet add package TenonAdmin
```

The current version is `0.1.1`, published to nuget.org. The core integration is just three lines:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` binds configuration and registers all the services — JWT, RBAC, data permissions, logging, and the rest; `MapTenonAdmin` mounts the routes, health checks, and (in dev) the OpenAPI docs. It runs on the default SQLite, zero-config.

To share sessions and cache across replicas (multi-instance deployment), also install `TenonAdmin.Caching.Redis` and call `AddTenonAdminRedisCache(builder.Configuration)` **before** `AddTenonAdmin` — the kernel's replaceable services are registered with `TryAdd`, first registration wins, so anything after `AddTenonAdmin` can't outrun the built-in in-process cache. Without `Cache:Provider=Redis` configured, that line is a no-op, so single-instance development is unaffected.

If you need finer-grained control over dependencies, you can reference a single layer instead (`.AspNetCore` / `.Services` / `.SqlSugar` / `.Core`). Why the packages are layered this way, and what "replaceable" actually means in practice, are covered in full in [Core Concepts](/guide/concepts); this page is only about getting it running.

> The API may still change before 1.0; breaking changes are marked clearly in the [Changelog](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md). Development happens on the `dev` branch.

## Run the frontend while you're at it

There are two official frontend templates, both against the same backend: `web/` is Vue 3 with Naive UI, `web-react/` is React 19 with Ant Design. They're feature-aligned, so pick whichever stack suits you — `web/` runs on `5173`, `web-react/` on `5174`:

::: code-group

```bash [Vue (web/)]
cd web
npm install
npm run dev
```

```bash [React (web-react/)]
cd web-react
npm install
npm run dev
```

:::

The built-in reverse proxy forwards `/api` and `/openapi` verbatim to the backend on `:5100` (override the target with the `TENON_API_TARGET` environment variable), so as far as the browser is concerned there's a single origin and local development needs no CORS. Open the matching port, log in with the same super-admin account and password, and you'll see the full admin UI.

When the frontend regenerates its API types (`npm run gen:api`), the backend must be running — it pulls the contract from a live `/openapi/v1.json`, not offline. Each template has its own `gen:api`, run from its own directory.

::: tip Want it as a one-off scaffold (the soybean / vite kind)?
The `cd` into a directory above is the "clone the repo, track upstream" path — recommended, since the frontend then evolves in lockstep with the NuGet-versioned backend contract. If you just want a copy to own and maintain yourself, degit a snapshot with no `.git` history as your starting point, whichever template you chose:

```bash
npx degit Tenon-Net/TenonAdmin/web my-web        # Vue template
npx degit Tenon-Net/TenonAdmin/web-react my-web  # React template
```

The trade-off is explicit: **no upgrade channel**. Upstream fixes are yours to read off the diff and reapply by hand, and the snapshot drifts from the NuGet-versioned backend contract. To keep pulling upstream fixes, don't snapshot — follow [Syncing Your Fork](/guide/sync-fork); for how the two templates differ and which to pick, see [Choosing a Frontend Template](/guide/frontend-templates).
:::

## Swap out the default database

Zero-config defaults to SQLite (`Data Source=./data/admin.db`, relative to the ContentRoot). Switching to a real database takes no code changes — the `TenonAdmin:Database` section decides it, and you change just two things, `DbType` and `ConnectionString` (`Sqlite` / `MySql` / `SqlServer` / `PostgreSQL` are all supported):

```json
{
  "TenonAdmin": {
    "Database": {
      "DbType": "MySql",
      "ConnectionString": "Server=127.0.0.1;Port=3306;Database=tenon;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;"
    }
  }
}
```

Containerized deployment is smoother with environment variables (double underscores for nesting):

```bash
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
```

Switching dialect is still **one** connection. To attach a log or legacy database in the same process, see [Configure Multiple Databases](/guide/multi-database).

::: warning Production won't auto-create tables
When `ASPNETCORE_ENVIRONMENT=Production`, tables are **not** auto-created even with CodeFirst enabled — this is a safety gate against altering the schema by accident in production. For the first deploy against an empty database, either turn on `EnableCodeFirstInProduction: true` temporarily to let it build the schema once, or have a DBA create it by hand. See the [Deployment guide](/guide/deployment/) for details.
:::

With the kernel running and the database swapped, the next stop is adding your own business module on top of it, end to end — see [Add a Business Module](/guide/business-module).
