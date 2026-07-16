# Deployment: Choose a Route, Then Clear the Security Baseline

You've already got it running locally via `dotnet new tenon-app` (or three lines of `Program.cs`), and now you need to ship it to a server. `npm run dev` works because the Vite dev server reverse-proxies `/api` and `/openapi` to the backend (`web/vite.config.ts`) — that proxy layer only exists during development. The build output `web/dist` is a pile of static files, and who hosts it and how it finds the backend are the two questions going live has to answer. This page first helps you pick a hosting route by those two questions, then walks the security baseline that no route escapes.

## Pick a hosting route

The four options differ on just two points: who hosts the frontend build, and whether frontend and backend are same-origin.

| Route | Who hosts the frontend | Same-origin | When to pick it |
|---|---|---|---|
| [Route A: Monolithic](/guide/deployment/route-a) | The backend process itself (`UseStaticFiles`) | Yes | One process, one port — least fuss for an internal system |
| [Route B: Reverse Proxy](/guide/deployment/route-b) | nginx / Caddy | Yes | You have a gateway already, or want Caddy to auto-issue TLS certs |
| [Route C: True Cross-Origin (CDN)](/guide/deployment/route-c) | CDN / separate domain | No | Frontend on a CDN — the only route that needs CORS |
| [Containers & Multi-Replica](/guide/deployment/docker) | Caddy in a container | Yes | Going to Docker / K8s, or scaling horizontally |

Same-origin (A, B) is the easy path: `web/dist` requests the backend same-origin by default (`baseUrl` in `src/api/client.ts` is empty, and paths already include `/api/v1`), so no CORS. Only Route C has frontend and backend on different origins, and only then do both sides need CORS configured.

::: tip Where the old "Route D" went
The Docker route, along with multi-replica deployment, has been folded into [Containers & Multi-Replica](/guide/deployment/docker) — there's no separate "Route D" anymore. For containers, go straight to that page.
:::

The first step is the same for all four routes: build the frontend first:

```bash
cd web
npm ci
npm run build     # output goes to web/dist/
```

## The security baseline you must clear before going live

Whichever route you pick, none of the following can be dodged in production. For several of them the kernel "refuses to start unless satisfied," rather than running the process with the risk baked in.

| Setting | Why it must be dealt with |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | Unset = dev-key mode: the kernel auto-generates a key to `./data/dev-jwt.key` and prints a warning. Production must configure it explicitly (a random string ≥32 bytes), and it must not enter version control — use an environment variable or a secrets manager. |
| `TenonAdmin:Database` | Defaults to SQLite `./data/admin.db` (relative to the ContentRoot). For multiple instances or concurrent writes, switch to MySQL / SqlServer / PostgreSQL (change the two items `DbType` + `ConnectionString`). |
| `TenonAdmin:Id:WorkerId` | The snowflake generator's machine bit. A single instance can leave it unset (falls back to 0); when scaling horizontally every replica must differ (0–63), or same-millisecond issuance collides on the primary key. Configuring Redis without giving it explicitly refuses startup outright — see [Containers & Multi-Replica](/guide/deployment/docker) for the details. |
| `TenonAdmin:Upload:RootPath` | Defaults to `./wwwroot/upload`. Declare it as a data volume, or files are lost on redeploy; on Route A (backend also hosting the frontend) you must also move it out of `wwwroot`, or uploaded files get served anonymously by the static middleware — see [Route A's auth-bypass warning](/guide/deployment/route-a). |
| `TenonAdmin:Api:ForwardedHeaders` | Required behind any reverse proxy / load balancer. Without it the backend always sees the proxy's single IP: every user shares one rate-limit bucket, per-IP brute-force protection drops to zero, and the audit log's IP column is void. For config details see [Route B](/guide/deployment/route-b). |
| `TenonAdmin:Cache:Provider` | A single instance can leave it `Memory`. Multiple replicas must switch to `Redis`, or forced logout, permission revocation, and login lockouts fail to propagate between replicas — and once they fail, they fail for days. See [Containers & Multi-Replica](/guide/deployment/docker) for the details. |

All of the above can go through environment variables, with double underscores for nesting (common in containerized deployments):

```bash
TenonAdmin__Jwt__SecretKey='...'
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
TenonAdmin__Upload__RootPath='/data/upload'
```

One item outside the table: `TenonAdmin:Database:SlowSqlMillis` (the slow-SQL warning threshold, default `1000` ms): any statement taking longer than that is logged at `Warning` along with its SQL and parameters; failed SQL is always logged at `Error` (with statement and parameters), unaffected by this setting and with no switch to turn it off. To observe every statement, lower it (e.g. `1`), but in production that drowns the logs. The log category is `TenonAdmin.Sql` — tune its level on its own if you want.

## The production table-creation gate: first-time creation and upgrade columns

Production has a table-creation safety gate: when `ASPNETCORE_ENVIRONMENT=Production`, tables aren't created or altered automatically even with `EnableCodeFirst=true` (true by default) — a production database is usually maintained by hand by a DBA, and the app shouldn't `ALTER` it on its own. To let it through, turn this on explicitly:

```json
{ "TenonAdmin": { "Database": { "EnableCodeFirstInProduction": true } } }
```

It defaults to false and governs two things:

- **First deploy to production against an empty database**: the tables don't exist yet, so the seed has nowhere to write. Either turn this on temporarily to let it create the tables and write the seed (you can turn it off again once created), or have a DBA create the tables named in the startup error first, then start.
- **Adding columns on a kernel-version upgrade**: a new kernel version may add columns to its own tables (adding fields is routine; dropping or narrowing columns never happens). Either turn this on for this startup to let it fill them in (CodeFirst only adds columns — never drops or narrows them — so it's safe for existing data), or have a DBA `ALTER TABLE ... ADD COLUMN` by hand for the tables and columns named in the error.

::: warning Not letting it through fails at startup by name — this is deliberate
Neither scenario starts up broken: an empty database with missing tables throws an error naming the tables (`...but the following tables the seed needs to write to don't exist: sys_schema_version, ...`), and an upgrade with missing columns throws an error naming the columns (`schema is behind the current entities; the following table is missing columns: sys_user(Avatar)`) — just take one of the two options it tells you. The reason it would rather blow up at startup is that letting it slide means the process comes up fine and only blows up at the driver layer's "column doesn't exist" the first time that table is queried — an error with no table name and no column name, leaving no one able to tell what to ALTER. The guard only checks for missing columns, not changes to type / length / nullability: a DBA who deliberately widened a `varchar` or added their own column won't be flagged.
:::

On the first seed write, if `TenonAdmin:Seed:AdminPassword` isn't explicitly configured, the console prints a random super-admin password once (16 characters, shown just that once) — be sure to keep it. To fix the account and password, configure it.

### How seed data is handled on upgrade

Seeding is insert-only by default (existence checked by primary key), so seed rows the kernel **adds** (a new menu, a new config item) flow into your database automatically after an upgrade — nothing to do. Rows the kernel **changes** (moving a permission button under a different page, adding an icon to a built-in module) are driven by the `sys_schema_version` version gate: once the kernel bumps the seed version, the next startup refreshes the built-in rows of the two structural tables — the menu tree and modules — back to the new shape, then writes the version number back.

::: warning Your edits to built-in menus get refreshed away on upgrade
Title / order / icon edits you made to **built-in menus** in the menu-management page are refreshed back to the kernel's values on a kernel upgrade (those rows belong to the kernel). Menus you added yourself are unaffected. The config center (`sys_config`), dictionaries, users, and role grants are your data — an upgrade doesn't touch a single row of it.
:::

## Post-go-live self-check

Three curls confirm the whole chain works:

```bash
curl https://<your-domain>/health         # Healthy: process alive
curl https://<your-domain>/health/ready   # Healthy: DB + cache both reachable
curl -i https://<your-domain>/api/v1/ping # 401: API routing works (this endpoint requires login)
```

`/health` and `/health/ready` have different semantics, so don't probe the wrong one: `/health` only checks whether the process itself is still responding (matching k8s's livenessProbe, process-level restart); `/health/ready` actually connects to the database and cache (matching readinessProbe, load-balancer node removal). To decide "can it take traffic," probe the latter.

Then open the frontend and log in once; getting a menu back means the JWT secret, database, and seed data all line up.

One last easy false alarm: a 404 on `/openapi/v1.json` in production is expected behavior, not something missing from the deployment. It's only mounted in the Development environment as the contract source for the frontend's `npm run gen:api`, not a production endpoint.
