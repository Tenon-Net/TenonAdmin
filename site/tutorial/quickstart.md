# Get Your First Endpoint Running from Scratch

This guide walks you through running the TenonAdmin kernel, getting a usable token, and calling your first protected endpoint. Everything is zero-config — no database installation required.

::: tip Prerequisites
- .NET 10 SDK
- Node.js (20+ recommended) if you also want to run the frontend
:::

## 1. Run the sample project bundled with the repo

After cloning the repo, just run `backend/samples/MinimalHost` — the minimal sample host in the repo, whose `Program.cs` is only a few lines of wiring:

```bash
dotnet run --project backend/samples/MinimalHost
```

On first startup it automatically:

- Creates tables via the default SQLite (CodeFirst; the database file lands under `./data/` inside `backend/samples/MinimalHost`)
- Writes seed data (menus, roles, the super-admin account, etc.)
- **Prints a randomly generated super-admin password to the console** — printed only once, so copy it down

The service listens on `http://localhost:5100` by default.

::: warning The password is printed only once
Don't panic if you missed it — just delete the database file under `backend/samples/MinimalHost/data` and `dotnet run` again to reseed (only do this in a local experimentation environment; never do this to a production database).
:::

## 2. Verify three endpoints

Once the service is up, confirm these three probes respond:

```bash
# Liveness probe, runs no dependency checks
curl http://localhost:5100/health

# Readiness probe: healthy only if both DB and cache are connected
curl http://localhost:5100/health/ready

# OpenAPI contract (mounted only in Development, the source for the frontend's gen:api)
curl http://localhost:5100/openapi/v1.json
```

The first two should both return `Healthy`. `/openapi/v1.json` returns a large blob of JSON — the contract source the frontend will later use to generate types.

## 3. Log in to get a token

`GET /api/v1/ping` is the smallest protected endpoint in the kernel — it only lets you through with a valid token. The captcha is disabled by default (`Security:Captcha:Enabled` defaults to off), so login only needs an account and password:

```bash
curl -X POST http://localhost:5100/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"account":"superAdmin","password":"<the password printed to the console>"}'
```

`data.accessToken` in the response envelope is your token:

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": true } }
```

`superAdmin` is the default super-admin account (the default value of `TenonAdmin:Seed:AdminAccount`). On first login `mustChangePassword` will be `true` — for now we're just getting the endpoint working, so ignore it.

## 4. Call your first protected endpoint

Attach the token you just obtained:

```bash
curl http://localhost:5100/api/v1/ping \
  -H "Authorization: Bearer <accessToken>"
```

Response:

```json
{ "code": 0, "data": { "pong": true, "account": "superAdmin", "at": "2026-07-...T..." } }
```

Without a token, or with an expired/revoked one, you get a `401` (standard envelope, `code=40006`). The super admin (the `sadm` claim) automatically bypasses the subsequent `[RolePermission]` permission-code check; a regular user first needs the corresponding route attached in menu management and granted in role management before they can call the same endpoint — the full write-up of this chain is in [Add a Business Module](/tutorial/business-module).

## 5. Integrate into an existing ASP.NET Core project in three lines

What you ran above is the sample host bundled with the repo. To actually wire the kernel into your own project, it comes down to three lines:

```bash
dotnet add package TenonAdmin
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` binds configuration and registers all services — JWT, RBAC, data permissions, logging, and so on; `MapTenonAdmin` mounts routes, health checks, and (in dev) the OpenAPI docs. It runs zero-config with the default SQLite; to switch databases, just change `TenonAdmin:Database` in `appsettings.json`.

The current version is **`0.1.0`**, published to nuget.org; the API may still change before 1.0, and development happens on the `dev` branch.

## 6. Also run the frontend

The frontend is the Vue 3 + Naive UI admin template in the repo's `web/` directory:

```bash
cd web
npm install
npm run dev        # Vite runs on :5173, auto-proxying /api and /openapi to the backend on :5100 (override the target with TENON_API_TARGET)
```

Open `http://localhost:5173` in your browser and log in with the same super-admin account and password to see the full admin UI.

::: tip One command to start everything
`dev.bat` at the repo root starts the backend + frontend in two separate windows (installing frontend dependencies on first run); `stop.bat` stops them.
:::

## Next steps

- Want to add your own business tables and endpoints on top of the kernel → [Add a Business Module End-to-End](/tutorial/business-module)
- Want to add a real page to the frontend → [Add a Frontend Page](/tutorial/frontend-page)
- Want to ship it to a server → [Full Container Deployment Walkthrough](/tutorial/docker-deploy)
- Want to understand how "replaceability" actually works and why packages are layered this way → [Core Concepts](/guide/concepts)
