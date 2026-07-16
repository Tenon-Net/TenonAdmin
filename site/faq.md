# FAQ

This page collects the real "first questions" — the handful of things most likely to trip you up right after installing the package and running the kernel for the first time. Most other problems are hiding in the raw error text: for expected configuration mistakes (the table-creation gate, a missing `WorkerId`, and the like) the kernel names the specific config item / table / column rather than throwing a generic exception, and reading along with it is usually faster than digging through the docs. If you genuinely can't make it out, search the repo's [issues](https://github.com/Tenon-Net/TenonAdmin/issues) for keywords; when opening a new issue, include your .NET / Node version, `TenonAdmin:Database:DbType`, whether it's single-instance or multi-replica, and the full error stack trace — it saves a round trip.

## What account and password do I log in with on first startup?

The account is `superAdmin`, and the password depends on which path you took — the password comes from a completely different place in these three cases, and if you can't log in it's usually because you've mistaken which path you're on:

- **Local clone running MinimalHost**: the repo only has the `appsettings.Development.json.example` template (the real file is gitignored, never committed), so **a fresh clone's first startup also takes the random-password path below**. For a fixed password, copy the template to `appsettings.Development.json` and fill in `Seed:AdminPassword` — the repo's dev convention value is `Aa123456` (what the frontend login form pre-fills in dev mode). Once set, no random password is printed, and the console just carries a plain "super admin created from configuration" log line.
- **Zero-config / production (no `Seed:AdminPassword`)**: the kernel generates a 16-character random password (with easily-confused characters like `0/O` and `1/l/I` stripped out so you can copy it from the log), printed inside a prominent box in the startup log via `LogWarning`, **only once, on the startup that actually creates the account**:

```text
╔══════════════════════════════════════════════════════╗
║  TenonAdmin first run — super admin created            ║
║  Account:  superAdmin
║  Password: xxxxxxxxxxxxxxxx
║  Shown only this once — change it right after login!   ║
╚══════════════════════════════════════════════════════╝
```

- **Compose demo environment (repo-root `docker-compose.yml`)**: the password comes from the `TENON_ADMIN_PASSWORD` environment variable, default `Tenon@123456`. To change it, override it in the `.env` file in the same directory.

To pin your own password (CI, automation), just configure `Seed:AdminPassword` before startup:

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "your-password" } } }
```

Only leaving it empty (the default) takes the random-generation path. The seed runs once, only when the `sys_user` table is empty: once any user exists, changing this config won't overwrite the existing account.

## I missed the first-startup log — how do I recover the random password?

You can't. The password was hashed when it went into the database — there's no plaintext to recover. Either edit the password hash on that super-admin record directly, or clear `sys_user` (or drop the database) to let the seed run again — configure `Seed:AdminPassword` for the replay so this time it uses your chosen value instead of a random one.

## Cloned locally but it won't run — where did `appsettings.Development.json` go?

It's excluded by `.gitignore` and not in version control — this file holds local credentials (the database connection string, the JWT secret) and shouldn't go into git. Just copy the neighboring `appsettings.Development.json.example` and rename it; for the sample host it's at `backend/samples/MinimalHost/appsettings.Development.json.example` — edit as needed.

## Where to find switching databases, gen:api, proxying, health checks

Each of these has its own page with the full detail; here are just the symptoms and where to land:

| What you want to do | Where to look |
|---|---|
| Switch the default SQLite to MySQL / SqlServer / PostgreSQL | The database-switching section of [Quick Start](/guide/getting-started) |
| `npm run gen:api` errors / generates the wrong types (start the backend first) | [Frontend API Contract](/frontend/api-contract) |
| Why `/api` works locally, and whether production needs CORS | [Request & Proxying](/frontend/request) |
| What `/health` and `/health/ready` each probe, and whether a production 404 on `/openapi` means something's missing | The go-live self-check in the [Deployment guide](/guide/deployment/) |
| A multi-replica startup errors with something about `WorkerId` | [Containers & Multi-Replica](/guide/deployment/docker) |
