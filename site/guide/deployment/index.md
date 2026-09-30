# Deployment: Choose a Route, Then Clear the Security Baseline

After the app runs locally, deployment requires frontend hosting, a backend service, and production configuration. The development Vite proxy is not part of the build output, so the server must route API requests to the backend explicitly. Examples below use Vue’s `web/`; React uses `web-react/`, with its build output in its own `dist/` directory.

## Pick a hosting route

The four options differ on just two points: who hosts the frontend build, and whether frontend and backend are same-origin.

| Route | Who hosts the frontend | Same-origin | When to pick it |
|---|---|---|---|
| [Route A: Monolithic](/guide/deployment/route-a) | The backend process itself (`UseStaticFiles`) | Yes | One process, one port — least fuss for an internal system |
| [Route B: Reverse Proxy](/guide/deployment/route-b) | nginx / Caddy | Yes | You have a gateway already, or want Caddy to auto-issue TLS certs |
| [Route C: True Cross-Origin (CDN)](/guide/deployment/route-c) | CDN / separate domain | No | Frontend on a CDN — the only route that needs CORS |
| [Containers & Multi-Replica](/guide/deployment/docker) | Caddy in a container | Yes | Going to Docker / K8s, or scaling horizontally |

Same-origin (A, B) is the easy path: `web/dist` requests the backend same-origin by default (`baseUrl` in `src/api/client.ts` is empty, and paths already include `/api/v1`), so no CORS. Only Route C has frontend and backend on different origins, and only then do both sides need CORS configured.

The first step is the same for all four routes: build the frontend first:

```bash
cd web
npm ci
npm run build     # output goes to web/dist/
```

## The security baseline you must clear before going live

Choose a hosting route, then configure each item below. Missing requirements such as a JWT secret prevent startup; upload storage, proxy trust, and cache propagation also need verification through real requests.

| Setting | Why it must be dealt with |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | Unset in production (any non-Development environment) **refuses to start** — it throws outright. Only the Development environment auto-generates a key to `./data/dev-jwt.key` and prints a warning. Production must configure it explicitly (a random string ≥32 bytes), and it must not enter version control — use an environment variable or a secrets manager. |
| `TenonAdmin:Database` | Defaults to SQLite `./data/admin.db` (relative to the ContentRoot). For multiple instances or concurrent writes, switch to MySQL / SqlServer / PostgreSQL (change the two items `DbType` + `ConnectionString`). |
| `TenonAdmin:Id:WorkerId` | The snowflake generator's machine bit. Unset, a file lock spreads processes on one machine and `sys_worker_lease` claims a free slot across a shared database. Two instances configured with the same number: the second will not boot. See [Containers & Multi-Replica](/guide/deployment/docker). |
| `TenonAdmin:Upload:RootPath` | Defaults to `./wwwroot/upload`. Declare it as a data volume, or files are lost on redeploy; on Route A (backend also hosting the frontend) you must also move it out of `wwwroot`, or uploaded files get served anonymously by the static middleware — see [Route A's auth-bypass warning](/guide/deployment/route-a). |
| `TenonAdmin:Api:ForwardedHeaders` | Required behind any reverse proxy / load balancer. Without it the backend always sees the proxy's single IP: every user shares one rate-limit bucket, per-IP brute-force protection drops to zero, and the audit log's IP column is void. For config details see [Route B](/guide/deployment/route-b). |
| `TenonAdmin:Cache:Provider` | A single instance can leave it `Memory`. Multiple replicas must switch to `Redis`, or forced logout, permission revocation, and login lockouts fail to propagate between replicas — and once they fail, they fail for days. Changing this setting alone isn't enough: the host project also needs the `TenonAdmin.Caching.Redis` package installed, and a call to `AddTenonAdminRedisCache(builder.Configuration)` **before** `AddTenonAdmin()`. Miss either condition and it silently falls back to the in-process cache. See [Containers & Multi-Replica](/guide/deployment/docker) for the details. |

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
- **Adding columns on a kernel-version upgrade**: a new kernel version may add columns to its own tables (adding fields is routine; dropping or narrowing columns never happens). Either turn this on for this startup to let it fill them in (CodeFirst only adds columns — never drops or narrows them — but backup and migration validation are still required), or have a DBA `ALTER TABLE ... ADD COLUMN` by hand for the tables and columns named in the error.

::: tip Why evolved columns are nullable
Columns the kernel **adds to existing tables** always use a nullable database column (`IsNullable`). SQL Server cannot `ADD` a `NOT NULL` column without a default to a table that already has rows; after a nullable add, old rows are `NULL` and the read path treats that as the default (e.g. MFA flags become false, absolute expiry falls back to session `ExpiresAt`). New properties may use `T?`, but a released public property keeps its CLR type and locks the ORM's default-value mapping with a regression test. If a DBA adds the column by hand, prefer nullable too unless you also supply a `DEFAULT` and backfill existing rows.
:::

::: warning Not letting it through fails at startup by name — this is deliberate
Startup checks name missing tables or columns. A missing table message contains `种子要写的表在库中不存在`; a missing column message contains `库表结构落后于当前实体`. Correct the schema before restarting so the failure does not first surface during a business request.

The check covers missing columns, not changes to types, lengths, or nullability. Review database migrations separately; passing startup validation does not prove full schema compatibility.
:::

On the first seed write, if `TenonAdmin:Seed:AdminPassword` isn't explicitly configured, the console prints a random super-admin password once (16 characters, shown just that once) — be sure to keep it. To fix the account and password, configure it.

### How seed data is handled on upgrade

Seeding is insert-only by default (existence checked by primary key), so seed rows the kernel **adds** (a new menu, a new config item) flow into your database automatically after an upgrade — nothing to do. Rows the kernel **changes** (moving a permission button under a different page, adding an icon to a built-in module) are driven by the `sys_schema_version` version gate: once the kernel bumps the seed version, the next startup refreshes the built-in rows of the two structural tables — the menu tree and modules — back to the new shape, then writes the version number back.

::: warning Your edits to built-in menus get refreshed away on upgrade
When an upgrade synchronizes built-in structural seeds, framework values can overwrite your edits to built-in menu titles, ordering, and icons. Custom menus are outside those built-in rows. Configuration, dictionaries, users, and role grants follow their own data-maintenance rules; review target-version migration notes and back up before deployment.
:::

## Post-go-live self-check

First check the process, dependencies, and API routing with these requests, then sign in through the frontend to verify business access:

```bash
curl https://<your-domain>/health         # Healthy: process alive
curl https://<your-domain>/health/ready   # Healthy: DB + cache both reachable
curl -i https://<your-domain>/api/v1/ping # 401: API routing works (this endpoint requires login)
```

`/health` and `/health/ready` have different semantics, so don't probe the wrong one: `/health` only checks whether the process itself is still responding (matching k8s's livenessProbe, process-level restart); `/health/ready` actually connects to the database and cache (matching readinessProbe, load-balancer node removal). To decide "can it take traffic," probe the latter.

Then open the frontend and log in once; getting a menu back means the JWT secret, database, and seed data all line up.

One last easy false alarm: a 404 on `/openapi/v1.json` in production is expected behavior, not something missing from the deployment. It's only mounted in the Development environment as the contract source for the frontend's `npm run gen:api`, not a production endpoint.

## Rolling back

Before rollback, check whether the old version can read the current schema and data, and prepare backups of the database and uploaded files. NuGet applications need a build using the older packages; container deployments need the older image. Additive schema changes help compatibility but do not make business migrations, seed changes, or external-service changes reversible. Rehearse rollback in a test environment before production.

What you genuinely can't undo is the publish step itself. Once a tag is pushed, it has already triggered `backend-release` to push a package to nuget.org — a package can be unlisted, never deleted. The full cadence is in the [changelog](/changelog) and the [release runbook](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/releasing.md). Rolling back rewinds the instance you deployed, not a package that's already out the door.
