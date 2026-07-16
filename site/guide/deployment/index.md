# Deployment

For **kernel consumers**: you've already got local dev running via `dotnet new tenon-app` (or three lines of `Program.cs`), and now you need to ship it to a server.

`npm run dev` works out of the box because the Vite dev server proxies `/api` and `/openapi` to the backend (`web/vite.config.ts`) — **this proxy layer only exists during development**. The build output `web/dist` is just a pile of static files; who hosts it and how it finds the backend are the two questions deployment must answer. This guide gives three routes — pick one.

::: tip Want to go straight to containers
Jump to **Route D** (end of this doc) — the repo root already has a `Dockerfile` + `docker-compose.yml` that bring up the whole stack with one command.
:::

## 0. Must-do before going live (security baseline)

| Setting | Why it must be changed |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | Leaving it unset = **dev key mode**: a key is auto-generated to `./data/dev-jwt.key` with a warning printed. Production must configure it explicitly (a random string ≥32 bytes), and it **must not go into version control** — use an environment variable or a secrets manager. |
| `TenonAdmin:Database` | Defaults to SQLite at `./data/admin.db`. Switch to MySQL / SqlServer / PostgreSQL for multiple instances or concurrent writes. |
| `TenonAdmin:Id:WorkerId` | **Must differ per instance (0–63) when scaling horizontally**, or instances generating an ID in the same millisecond will collide. A single instance can leave it unset (it falls back to 0); but **configuring Redis without explicitly setting this → startup fails outright** (see "Multi-replica deployment"). |
| `TenonAdmin:Upload:RootPath` | Defaults to `./wwwroot/upload`. Declare it as a data volume (otherwise files are lost on redeploy); **if using Route A, it must also be moved out of `wwwroot`** — see the warning below. |
| `TenonAdmin:Api:ForwardedHeaders` | **Must be configured behind any reverse proxy/load balancer.** Without it, the backend always sees the proxy's single IP: every user shares one rate-limit bucket, per-IP brute-force protection is neutralized, and the audit log's IP column is meaningless. See "Behind a reverse proxy" below. |
| `TenonAdmin:Cache:Provider` | `Memory` is fine for a single instance. **Multiple replicas must switch to `Redis`** — otherwise forced logout, permission revocation, and login lockouts all fail to propagate across replicas (see "Multi-replica deployment"). |
| `TenonAdmin:Database:SlowSqlMillis` | Slow-SQL warning threshold, default `1000` (ms). Failed SQL is **always** logged at `Error` level (with statement and parameters), regardless of this setting. The log category is `TenonAdmin.Sql` — adjust its level independently if needed. |

### First production deploy: table creation must be explicitly allowed

Production has a **table-creation safety gate**: when `ASPNETCORE_ENVIRONMENT=Production`, tables are **not** auto-created even if `EnableCodeFirst=true` (production databases are usually maintained by hand by a DBA, and the app shouldn't ALTER them unprompted). So for the first deploy against an empty database, pick one of:

```json
{ "TenonAdmin": { "Database": { "EnableCodeFirstInProduction": true } } }
```

- **Let it create the tables itself**: enable this setting for the first startup (creates tables + writes seed data), then turn it off again once it starts successfully.
- **Have a DBA create them by hand**: build the schema yourself first, then start up with this setting left `false`.

::: warning Empty database + Production + this setting off = startup failure
The tables don't exist yet, so the seed data has nowhere to go. The kernel probes the tables at startup and throws an error naming the missing ones (`...but the following tables the seed needs to write to don't exist: sys_schema_version, ...`); just follow the error and pick one of the two options above. The log also carries a warning that automatic CodeFirst table creation was skipped.
:::

The first startup writes seed data and **prints a random super-admin password once** to the console — save it. To set your own, use `TenonAdmin:Seed:AdminPassword`.

### Upgrading the kernel version: adding columns

A new kernel version may **add columns** to its own tables (adding fields is routine; dropping/narrowing columns never happens). Since production's table-creation gate is off by default, **nothing fills in that new column for you automatically**.

On upgrade, pick one of the same two options as the first deploy:

- **Let it add the column itself**: enable `EnableCodeFirstInProduction` for this startup, then turn it off again once it starts successfully. CodeFirst **only adds columns — it never drops or narrows them** — so it's safe for existing data.
- **Have a DBA add it by hand**: run `ALTER TABLE ... ADD COLUMN` for the tables/columns named in the startup error, then start up.

::: warning What happens if you don't add it
Startup **fails outright**, with an error naming the specific table and column (`schema is behind the current entities; the following table is missing columns: sys_user(Avatar)`). This is deliberate — letting it slide would mean the process **starts up fine**, only to blow up at the driver level with a "column does not exist" error the first time that table is queried, an error with no table name and no column name that leaves you guessing what to ALTER.

The guard only checks for **missing columns** — not changes to type, length, or nullability, so a DBA who deliberately widened a `varchar` or added their own column won't be flagged.
:::

### Upgrading the kernel version: seed data

Seeding is **insert-only by default** (checked by primary key), so:

- **New** seed rows added by the kernel (a new menu, a new config item) — flow into your database automatically after upgrading; nothing to do.
- **Changes to existing rows** made by the kernel (moving a permission button under a different page, adding an icon to a built-in module) — driven by the `sys_schema_version` version gate: once the kernel bumps its seed version, the next startup refreshes the built-in rows of the two structural tables — **the menu tree and modules** — back to the new shape, then writes back the version number.

::: warning The trade-off
Any title/order/icon edits you made to **built-in menus** in the menu-management page get overwritten back to the kernel's values on a kernel upgrade (the kernel owns those rows). **Menus you added yourself are unaffected.**

✅ **Never touched**: the config center (`sys_config`), dictionaries, users, role assignments — this is your data, and upgrades **never touch a single row** of it.
:::

Configuration can be driven entirely by environment variables, with double underscores for nesting:

```bash
TenonAdmin__Jwt__SecretKey='...'
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
TenonAdmin__Upload__RootPath='/data/upload'
```

## 1. Build the frontend

```bash
cd web
npm ci
npm run build     # output goes to web/dist/
```

By default `web/dist` requests the backend **same-origin** (`baseUrl` in `src/api/client.ts` is empty, and paths already include `/api/v1`). Routes A and B are both same-origin, so **no CORS configuration is needed**; only Route C needs it.

## In this section

- [Route A: Monolithic Deployment](/guide/deployment/route-a)
- [Route B: Reverse Proxy (nginx or Caddy)](/guide/deployment/route-b)
- [Route C: True Cross-Origin (CDN)](/guide/deployment/route-c)
- [Route D: Docker](/guide/deployment/route-d)
- [Multi-Replica Deployment](/guide/deployment/multi-replica)
- [Post-Deploy Self-Check](/guide/deployment/post-deploy-check)

**Next:** [Route A: Monolithic Deployment](/guide/deployment/route-a)
