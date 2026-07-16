# FAQ

::: tip
The questions below are grouped by topic and reflect the kernel's actual behavior. If you can't find an answer here, check the raw error message first (the kernel's errors for expected misconfigurations usually name the specific config item / table / column), then search the repo's [issues](https://github.com/Tenon-Net/TenonAdmin/issues) for keywords.
:::

## Preface: how to troubleshoot

- Read the full error message first. For expected configuration errors (table-creation gates, missing `WorkerId`, etc.) the kernel names the specific config item involved, rather than throwing a generic exception.
- Distinguish **kernel behavior** from **consumer code** issues: kernel-related symptoms can be checked against this page and the [Deployment Guide](/guide/deployment/); issues in consumer business code (your own controllers/services) are out of scope here.
- When asking for help, include: .NET / Node version, `TenonAdmin:Database:DbType`, whether it's a single instance or multi-replica deployment, and the full error stack trace.

## First Startup

### Where do I find the super-admin password?

**Symptom**: After first startup, you don't know which account/password to log in with.

**Cause**: Seeding runs once, only when the `sys_user` table is empty. If no password is explicitly configured, the kernel generates a random 16-character password and prints it to the console log **on that one startup**, in a format like:

```text
╔══════════════════════════════════════════════════════╗
║  TenonAdmin first startup, super admin created          ║
║  Account: superAdmin
║  Password: xxxxxxxxxxxxxxxx
║  This password is shown only this once — change it right after logging in!  ║
╚══════════════════════════════════════════════════════╝
```

**Fix**:

- If you missed that one log line — the password is already stored (hashed) and cannot be recovered as plaintext; you'll need to edit the database directly or wipe it and reseed.
- To pin a fixed password (e.g. for CI/automation), configure it before startup:

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "your-password" } } }
```

Only when `AdminPassword` is left empty (the default) does the kernel take the random-generate-and-print path; once the database already has any user, seeding won't run again, and changing the config won't overwrite an existing account.

### Where did `appsettings.Development.json` go — why can't I find it?

**Symptom**: After cloning the repo, it won't run locally, or the file is missing.

**Cause**: `appsettings.Development.json` is excluded via `.gitignore` — it's a local-dev credentials file (DB connection string, JWT secret, etc.) that shouldn't be committed.

**Fix**: Copy the neighboring `appsettings.Development.json.example` file, rename it, and edit as needed. The sample lives in `backend/samples/MinimalHost/appsettings.Development.json.example`.

## Database

### How do I switch the default SQLite to MySQL / SqlServer / PostgreSQL?

**Symptom**: Zero-config setup defaults to SQLite (`./data/admin.db`), and you want to switch to a production database.

**Cause**: The database type and connection string are both driven by the `TenonAdmin:Database` config section — no code changes needed.

**Fix**: Change the `DbType` and `ConnectionString` values (`Sqlite` / `MySql` / `SqlServer` / `PostgreSQL` are all supported):

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

You can also use environment variables (double-underscore nesting, convenient for containerized deployments):

```bash
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
```

::: warning Extra care in production
When `ASPNETCORE_ENVIRONMENT=Production`, tables are **not** auto-created even if `EnableCodeFirst=true` — for a first deploy against an empty database, either temporarily set `EnableCodeFirstInProduction: true` to let it create the tables itself, or have a DBA create them manually. See [Deployment Guide §0](/guide/deployment/) for details.
:::

## Distributed IDs

### Why does startup fail on a multi-replica deployment with a `WorkerId`-related error?

**Symptom**: Runs fine as a single instance, but fails to start after adding Redis caching and spinning up a second replica.

**Cause**: The snowflake algorithm's `WorkerId` (`TenonAdmin:Id:WorkerId`) determines the ID generator's machine bit. Leaving it unset is fine for a single instance (it falls back to 0). But once `Cache:Provider=Redis` is configured (a clear signal of multi-instance intent) without an **explicit** `WorkerId`, the kernel refuses to start outright — because silently allowing it would likely give both replicas `WorkerId=0`, and IDs generated in the same millisecond would collide on the primary key, silently.

**Fix**: Explicitly configure a distinct `WorkerId` (range 0–63) for each replica:

```bash
# Replica 0
TenonAdmin__Id__WorkerId=0
# Replica 1
TenonAdmin__Id__WorkerId=1
```

For Docker Compose, `--scale app=2` can't give each replica a different environment variable — split it into multiple explicit `app` services configured individually (see `docker-compose.scale.yml` at the repo root). For Kubernetes, use a StatefulSet and inject the value from the pod ordinal (`app-0`/`app-1`).

## Frontend API Contract

### `npm run gen:api` runs but errors out or generates the wrong content — what's going on?

**Symptom**: Running `npm run gen:api` to regenerate `src/api/schema.d.ts`, but it can't fetch data.

**Cause**: This command generates types by pulling the contract from a **running** backend instance's `/openapi/v1.json` — it's not offline generation. If the backend isn't running, that endpoint is unreachable.

**Fix**: Start the backend in another terminal first (`dotnet run --project backend/samples/MinimalHost`, or use `dev.bat` to start both backend and frontend at once), confirm `http://localhost:5100/openapi/v1.json` is reachable, then run `npm run gen:api`.

::: warning
`src/api/schema.d.ts` is a generated artifact — **do not hand-edit it**, any edits will be overwritten the next time `gen:api` runs. To change the types, modify the backend's endpoints/DTOs and regenerate.
:::

### Why does `/openapi/v1.json` return 404 in production — did the deployment miss something?

**Symptom**: Requesting `/openapi/v1.json` in production returns 404, making it look like the contract is missing.

**Cause**: This endpoint is **only mounted in the Development environment** — it's the contract source for `npm run gen:api` during local frontend development, not a production-facing API. A 404 in production is expected behavior, not a bug.

**Fix**: No action needed. To verify the backend itself is alive, use `/health` (liveness probe) or `/health/ready` (checks both database and cache connectivity).

## CORS and Proxying

### Why do frontend requests to `/api` work during local development — how is it proxied to the backend?

**Symptom**: The frontend runs on `:5173` and the backend on `:5100`, yet pages issue relative-path requests to `/api/...` and get data back with no CORS configured.

**Cause**: The Vite dev server used by `npm run dev` has a built-in reverse proxy (`web/vite.config.ts`) that forwards the `/api` and `/openapi` prefixes as-is to the backend address (defaults to `http://localhost:5100`, overridable via the `TENON_API_TARGET` environment variable). From the browser's perspective, there's only ever one origin (`:5173`), so CORS never comes into play.

```ts
// web/vite.config.ts
server: {
  port: 5173,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
}
```

**Note**: This proxy layer exists **only during development**. The production build (`web/dist`) is pure static files — who hosts it and how requests reach the backend is up to your deployment. Common approaches: have the backend also host the frontend build (same origin), or reverse-proxy through nginx/Caddy (still same origin) — neither requires CORS configuration. You only need to touch `TenonAdmin:Api:Cors:AllowedOrigins` if the frontend and backend are deployed on **different origins** (e.g. frontend on a CDN, backend on its own domain). See [Route C: True Cross-Origin](/guide/deployment/route-c) for the full setup.

## Health Checks

### What's the difference between `/health` and `/health/ready`, and which one should I probe?

**Symptom**: Configuring health-check probes for a container orchestrator, unsure which endpoint to hit.

**Cause**: The two have different semantics —

| Endpoint | Semantics | Checks |
|---|---|---|
| `/health` | Liveness | Whether the process itself is still responding |
| `/health/ready` | Readiness | Whether both database and cache connectivity are healthy |

**Fix**: Use `/health` for process-level restart policies (e.g. Kubernetes' `livenessProbe`); use `/health/ready` for deciding whether to accept traffic (`readinessProbe`, load balancer node removal). After deployment, you can smoke-test both:

```bash
curl https://<your-domain>/health         # Healthy
curl https://<your-domain>/health/ready   # Healthy
```
