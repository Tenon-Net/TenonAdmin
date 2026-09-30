# Quick Start

Run the repository sample to try TenonAdmin locally. The backend uses SQLite and creates the database, tables, and administrator account on first startup, so you do not need a separate database server. Start the backend and one frontend to sign in through your browser.

Install the .NET 10 SDK and Git. Node.js 22.12 or later is recommended for the frontend. Use matching frontend and backend releases, and keep extension packages at the same version as `TenonAdmin`. See the [changelog](/changelog) for releases; the `dev` branch may include unreleased features.

## Run the sample first

Run these commands in a terminal. Subsequent backend commands use the repository root as their working directory:

```bash
git clone --branch dev --single-branch https://github.com/Tenon-Net/TenonAdmin.git
cd TenonAdmin
dotnet run --project backend/samples/MinimalHost
```

Keep this terminal running. The listening message for `http://localhost:5100` confirms that the backend has started. SQLite data lives under `backend/samples/MinimalHost/data/`; the framework creates tables, initial menus, and roles.

When the administrator account is first created, the console shows `superAdmin` and a random password. **Save it now: it is displayed only during account creation.** If users already exist in the database, restarting does not generate a new password or overwrite their accounts.

## Start the frontend and sign in {#run-the-frontend-while-you-re-at-it}

Open another terminal at the repository root and choose one frontend. Vue uses Naive UI and React uses Ant Design. Both connect to the same backend; you only need one.

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

Open `http://localhost:5175` for Vue or `http://localhost:5174` for React. Sign in as `superAdmin` using the password you saved, then change the initial password. Reaching the admin interface and opening its menus confirms that the frontend can reach the backend.

After signing in, open User Management from the sidebar. The users below are sample data; on a fresh installation, confirm that your administrator appears. Later tutorials reuse this filter-and-table layout. The screenshot shows the Chinese interface.

[![Vue user management; click for full-size image](/screenshots/vue-admin.png)](/screenshots/vue-admin.png)

The development server forwards `/api` and `/openapi` to backend port `5100`, so local development needs no CORS configuration. Use `TENON_API_TARGET` if your backend runs elsewhere. If sign-in fails, check that the backend is still running and that you used the correct password source; see [FAQ](/faq).

Continue with [Core Concepts](/guide/concepts) or [Add a Business Module](/guide/business-module). To use a frontend as your own project, see [Choosing a Frontend Template](/guide/frontend-templates) and [Syncing Your Fork](/guide/sync-fork). The remaining API checks and configuration steps are optional references.

## Integrate into your own project in three lines

What you ran above is the sample bundled with the repo. To actually wire the kernel into your own ASP.NET Core project, first install the meta-package:

```bash
dotnet add package TenonAdmin
```

The following is a complete minimal startup file. In an existing project, register services before `Build()` and map endpoints before `Run()`:

```csharp
using TenonAdmin.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` binds configuration and registers all the services — JWT, RBAC, data permissions, logging, and the rest; `MapTenonAdmin` mounts the routes, health checks, and (in dev) the OpenAPI docs. It runs on the default SQLite, zero-config.

The default memory cache is sufficient for single-instance development. Before deploying multiple instances, follow [Containers & Multiple Replicas](/guide/deployment/docker) to configure Redis, registration order, shared storage, and instance IDs.

If you need finer-grained control over dependencies, you can reference a single layer instead (`.AspNetCore` / `.Services` / `.SqlSugar` / `.Core`). Why the packages are layered this way, and what "replaceable" actually means in practice, are covered in full in [Core Concepts](/guide/concepts); this page is only about getting it running.

> The API may still change before 1.0; breaking changes are marked clearly in the [Changelog](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md). Development happens on the `dev` branch.

## Check backend health and API descriptions {#verify-the-three-probes}

To check backend status or diagnose a connection problem, request these endpoints:

```bash
# Liveness probe — only checks the process is up, touches no dependencies
curl http://localhost:5100/health

# Readiness probe: returns Healthy only if both DB and cache are reachable
curl http://localhost:5100/health/ready

# OpenAPI contract, mounted only in Development, the source for the frontend's gen:api
curl http://localhost:5100/openapi/v1.json
```

The first two should return `Healthy`. `/openapi/v1.json` returns an OpenAPI JSON description that you'll use later to generate the frontend types; this endpoint isn't mounted in production, so a 404 against it there is expected behavior, not a missing setting.

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
Only in a disposable local environment, delete the database file under `backend/samples/MinimalHost/data` and `dotnet run` again; an empty database reseeds. You can't wipe a production database like that — use an existing administrator to reset the password and back up data before recovery.
:::

Want a fixed password you control (shared across a team, CI, repeated wipe-and-reseed)? Copy `backend/samples/MinimalHost/appsettings.Development.json.example` to `appsettings.Development.json` and fill in `Seed:AdminPassword`:

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "your-password" } } }
```

This file is excluded by `.gitignore` (it holds local credentials) and won't enter version control. With it set, the startup log no longer prints a random password, and you just log in with the account and password you chose. Note that the seed only recognizes an empty database: once any user exists, changing this won't overwrite the existing account — use the password change or reset flow for an existing account rather than editing seed configuration.

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

Without a valid token, or when the token has expired or the session has been revoked, the endpoint returns `401` with error code `40006` in the standard response. The super administrator’s `sadm` claim bypasses role permission checks. Regular users need the route configured in menu management and granted through a role; see [Add a Business Module](/guide/business-module).

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
